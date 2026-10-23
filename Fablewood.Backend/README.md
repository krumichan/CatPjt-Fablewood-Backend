# Fablewood 중앙 저장 API

ASP.NET Core 10 / Npgsql 10.0.3 / PostgreSQL 18. 이 서비스는 계정·접속 권한·원자 저장을 담당합니다. UE 전용 서버가 전투와 거래 규칙을 판정하고, API는 v8 SaveGame 바이트를 불투명한 데이터로 보관합니다. 클라이언트는 프로필·재화를 변경할 수 없습니다.

## 환경과 실행

- `FABLE_DB_CONNECTION`: PostgreSQL 연결 문자열. 비밀 파일/프로세스 환경으로만 전달합니다. 예: `Host=127.0.0.1;Port=55438;Database=fablewood;Username=<private-user>;Password=<private-password>;Maximum Pool Size=20;Timeout=5;Command Timeout=10`.
- `FABLE_DB_SCHEMA`: 기본 `fablewood`. ASCII 소문자·숫자·밑줄의 안전한 식별자만 허용합니다.
- `FABLE_SERVER_KEY_FILE`: 32~512자의 UTF-8 원문 키 파일. 앞뒤 공백은 제거합니다. 또는 `FABLE_SERVER_KEY` 중 하나만 설정합니다. 키를 Git, URL, 명령행 인자에 넣지 않습니다.
- `ASPNETCORE_URLS`: 개발 PC에서는 `http://127.0.0.1:5281`. 원격 접속은 HTTPS가 필수입니다. 프록시 전달 헤더를 자동으로 신뢰하지 않습니다.

`dotnet restore --locked-mode` 후 `dotnet build -c Release --no-restore`로 빌드합니다. 키가 없거나 약한 길이면 시작을 거절합니다. `Fablewood.Backend.exe --migrate`는 스키마 1을 트랜잭션으로 적용한 뒤 종료합니다. 성공 종료코드는 0이며, 서비스 시작은 별도 `Fablewood.Backend.exe`입니다. 마이그레이션은 자동 실행하지 않습니다. 운영 DB 계정에는 스키마 적용용 DDL 권한과 서비스용 최소 DML 권한을 분리하는 구성이 권장됩니다.

## API 계약

JSON은 camelCase이며 정의하지 않은 요청 필드는 400입니다. 계정명은 대소문자를 구분하지 않는 `[A-Za-z0-9_]` 3~32자, 비밀번호는 10~128자입니다. PBKDF2-SHA256 600,000회·개별 32바이트 salt·고정시간 비교를 사용합니다. 로그인과 가입에는 IP별 분당 20회 제한을 공유합니다.

| 요청 | 인증 | 요청 본문 / 응답 |
| --- | --- | --- |
| `GET /health` | 없음 | DB 연결·스키마1 확인 후 `{status:"ok"}`, 미준비 503 |
| `POST /v1/accounts` | 없음 | `{name,password,initialClass?:0..2}` → 201 `{playerId}` |
| `POST /v1/sessions` | 없음 | `{name,password}` → `{accessToken,expiresAt}`; 토큰 20분 |
| `POST /v1/join-tickets` | Bearer | `{}` → `{ticket,expiresAt}`; 입장권 180초(3분)·한 번 사용 |
| `GET /v1/me` | Bearer | `{playerId,revision,wallet,profileHash,leaseActive}`; 자기 지갑만, blob 미공개 |
| `POST /v1/server/claim` | 서버 키 | `{ticket,serverId}` → `{playerId,leaseId,leaseExpiry,revision,initialClass,profile,profileHash,wallet}` |
| `POST /v1/server/heartbeat` | 서버 키 | `{playerId,leaseId}` → `{leaseExpiry}`; DB 시각 기준 60초 갱신 |
| `POST /v1/server/commit` | 서버 키 | `{playerId,leaseId,operationId,expectedRevision,profile,wallet}` → `{revision,profileHash}` |
| `POST /v1/server/release` | 서버 키 | `{playerId,leaseId}` → `{released:true}`; 최신 저장 ACK 이후 호출 |

서버 키 헤더는 `X-Fable-Server-Key`입니다. 서버 키는 UE 전용 서버의 비공개 설정에만 전달합니다. 세션·입장권은 256비트 난수이며 DB에는 SHA256만 저장합니다. `profile`은 공백 없는 정규 Base64, 디코딩 기준 1~262144바이트입니다. `profileHash`는 raw bytes SHA256의 소문자 hex이며 새 프로필은 null입니다. 초기 클래스는 계정 생성 시 저장합니다.

`wallet`은 `{gold,ore,essence,rootCores,moonCrystals,healthPotions,manaPotions}`입니다. 골드 0~1,000,000, 광석·재료 0~100,000, 물약 0~20입니다. 새 계정은 재화 0, 체력 물약 3개, 마나 물약 2개입니다. API는 거래 가격을 추측하지 않으며, UE 서버가 계산한 스냅샷만 받습니다. 음수 지갑과 범위 초과는 DB 변경 전에 거절합니다.

## 동시 저장과 장애 복구

한 플레이어는 유효한 리스 하나만 가집니다. 재접속은 기존 리스가 해제되거나 만료될 때까지 거절합니다. 리스 UUID는 접속마다 새로 발급되며 만료 후 되살릴 수 없습니다. heartbeat를 20초 간격으로 보내고, 실패 시 저장·플레이 진행을 중단하는 것이 클라이언트 서버의 책임입니다.

commit은 프로필 행을 `SELECT FOR UPDATE`로 잠근 뒤 작업 ID·리스·리비전을 검사합니다. 프로필, 지갑, 작업 기록, 재화 차이 ledger를 같은 트랜잭션에서 확정합니다. 동일 작업 ID·동일 본문의 재전송은 처음 ACK를 반환합니다(이미 만료된 리스의 읽기 전용 replay도 허용). 동일 ID에 다른 본문은 409입니다. 다른 ID로 같은 리비전을 동시에 쓰면 하나만 성공합니다. 충돌 시 임의로 새 리비전을 붙여 덮어쓰지 말고 서버 세션을 재동기화해야 합니다.

오류는 `{code,message}`이며 인증 401, 원격 HTTP 403, 입력 범위 400, 리스/리비전/작업 충돌 409, DB 불가 503입니다. lease release 재전송은 이미 종료된 경우 409를 반환하며 다른 새 리스를 해제하지 않습니다. DB 오류 응답/로그에는 SQL 인자·비밀·프로필 바이트를 출력하지 않습니다. 유효기간이 지난 세션·입장권과 오래된 ledger 정리는 이 시제품에서 자동 삭제하지 않습니다.

## 집중 통합 테스트

형제 프로젝트 `Fablewood.Backend.IntegrationTests`는 외부 테스트 패키지 없이 실제 loopback HTTP + 고유 `fable_test_<uuid>` PostgreSQL 스키마를 사용합니다. 실행자가 DB를 준비한 뒤 `dotnet run --project ../Fablewood.Backend.IntegrationTests -c Release --no-build -- --output <새로운 중앙 증거 폴더>`를 실행합니다. 기존 출력 폴더는 거절하며 기존 게임 저장/제품 schema는 변경하지 않습니다.

검사 대상: 클라이언트/잘못된 서버 키 거절, 계정명 중복, 비밀번호 인증, 계정의 미정의 gold 필드 거절, 초기 지갑, 발급 응답과 DB 만료 시각 일치·180초(3분) 유효기간·단일사용/만료 입장권, 중복 접속, 음수/초과 지갑, 비정규/초과 profile, 동일 작업 재시도/다른 본문 충돌, 동시 revision 승자 한 명, ledger 실패시 전부 rollback, 리스 갱신/만료/이전 writer 차단, 타 계정 지갑 격리. 시간 만료·ledger 오류 주입은 오직 새 테스트 schema에서 수행합니다.

테스트는 `persistence-checkpoint.json`을 쓰고 schema를 보존합니다. 운영자가 해당 테스트 DB의 PostgreSQL 재시작을 수행한 다음 `--verify-persistence <checkpoint.json>`으로 새 연결에서 raw profile SHA·wallet·revision·operation/ledger 수를 비교합니다. 이 검사는 PostgreSQL을 직접 재시작하지 않으며 재시작 사실은 운영자가 별도 증거로 남겨야 합니다. 완료한 테스트 스키마/증거도 자동 삭제하지 않습니다.

구현 참고: [Npgsql 기본 사용법](https://www.npgsql.org/doc/basic-usage.html), [ASP.NET Core 10 rate limiting](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).
