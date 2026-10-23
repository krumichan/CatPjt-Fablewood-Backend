# CatPjt-Fablewood-Backend

Fablewood의 계정·접속 티켓·중앙 저장 API입니다. ASP.NET Core 10, Npgsql 10.0.3, PostgreSQL 18을 사용합니다. 게임 전투·이동·거래 가격·보상은 형제 `CatPjt-Fablewood-Game`의 Unreal 전용 서버가 판정하고, API는 프로필·재화·장비 소유권과 변경 이력을 원자적으로 저장합니다.

현재 경로는 장비 중앙 저장 확장을 위한 별도 개발 사본 `D:\GameProjects\Fablewood-Inventory-20261023\CatPjt-Fablewood-Backend`입니다. 원본 `D:\GameProjects\Fablewood`와 Git 등록 중인 `CatPjt-Fablewood-*` 저장소, 기존 API/PostgreSQL과 계정·비밀 설정은 보존합니다. 초기 분리 이력은 형제 게임의 [DevelopmentHistory.md](../CatPjt-Fablewood-Game/Docs/DevelopmentHistory.md)에 남아 있습니다.

## 현재 검증 상태

- API·통합 검사 프로젝트 잠금 복원 빌드: 경고 0개·오류 0개.
- 실제 HTTP/PostgreSQL 통합 검사: 316개 통과(기존 65 + 장비 확장 251).
- 새 별도 PostgreSQL 재시작 후 지속성 검사: 23개 통과.
- 형제 게임의 Editor 빌드와 native 검사 18개 통과.

실제 UE 검사도 별도로 통과했습니다. Editor-Cmd 전용 서버 1개·Editor 게임 클라이언트 2개가 판매·수리·강화·창고 입출고와 fresh/reconnect/resume을 완료했습니다. 원본의 실제 중앙 저장 v8 복사본 두 개는 새 계정에서 v0/null 상태와 원래 바이트 해시를 확인한 뒤, UUID 백필·v9 이전·재접속·API/서버 재시작 후 기존 재화와 장비 값을 보존했습니다. API 계약 fixture 검사와 실제 UE 이전 검사를 구분합니다. 새 패키지·독립 Server Target·Linux·외부망은 이번 검증 범위 밖입니다.

## 구성과 빌드

- `Fablewood.Backend/`: API, SQL migration 1·2, 패키지 lock, [상세 API 계약](Fablewood.Backend/README.md).
- `Fablewood.Backend.IntegrationTests/`: 실제 HTTP/PostgreSQL 검사. 실행 때 고유 `fable_test_<uuid>` 스키마를 사용합니다.
- `Tools/Start-Backend.ps1`, `Start-Backend.cmd`: Windows 실행기.
- `compose.yaml`, `.env.example`: PostgreSQL 정의와 비밀 없는 예시.
- `global.json`: .NET SDK `10.0.401`, `latestPatch`, 미리 보기 제외.

이 폴더에서 .NET SDK와 PowerShell 7.4 이상을 사용합니다. lock 파일을 갱신하지 않는 빌드 명령입니다.

```powershell
dotnet restore .\Fablewood.Backend\Fablewood.Backend.csproj --locked-mode
dotnet restore .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj --locked-mode
dotnet build .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj -c Release --no-restore
```

상대 프로젝트 참조로 API도 함께 빌드됩니다. 출력은 각 프로젝트의 `bin/Release/net10.0/`이며 실행기는 자동 빌드하지 않습니다. 현재 개발 검증에서 생성한 `bin/obj`·실행 상태가 있더라도 소스 배포에는 포함하지 않습니다.

## 장비 저장 계약

DB 스키마는 2, 정형 `inventoryVersion`은 1, 새 UE SaveGame은 v9입니다. 아이템 UUID·소유자·기존 로컬 ID·가방/창고 위치·장착 참조와 옵션·품질·등급·강화·내구도를 정형 테이블에 저장합니다. `POST /v1/server/commit-inventory`는 profile 행 잠금, 단일 writer 리스, expected revision, 작업 ID/전체 요청 해시를 확인하고 blob·재화 7종·장비/옵션·receipt·경제 ledger·장비 journal을 한 트랜잭션에서 확정합니다.

UUID 소유자는 서버 인증과 리스로 확인한 플레이어이며 클라이언트가 지정하지 않습니다. 제거된 아이템은 소유 이력을 남기고 UUID·로컬 ID 재사용을 거절합니다. 슬롯·직업·품질·등급·옵션·내구도 범위와 장착 위치도 검증합니다. `GET /v1/me/inventory`는 Bearer 계정 자신의 목록만 반환합니다. 게임 가격·생성/보상 조건은 UE 권위이며 API가 다시 계산하지 않습니다.

아직 이전하지 않은 중앙 프로필은 `inventoryVersion:0, inventory:null`입니다. UE가 기존 v8 이하 blob을 먼저 검증한 후 원래 장비 값·옵션·강화·내구도·로컬 ID·배열 순서를 유지하며 누락 UUID를 부여하고 첫 정형 백필 ACK 전에는 게임을 시작하지 않습니다. 이후 claim에서는 원본 v9 blob과 정형 목록의 전체 필드/순서/장착 참조를 대조하고 불일치를 덮어쓰지 않습니다. API는 Unreal blob을 해독하지 않으며 이 의미적 대조는 UE가 수행합니다. 로컬 `.sav` 자동 업로드는 없습니다.

기존 `/v1/server/commit`와 그 요청 해시는 유지합니다. 정형 이전 후에는 새 legacy mutation을 거절하되 이미 성공한 동일 작업 receipt의 읽기 전용 replay는 보존합니다. 같은 ID에 다른 payload, 이전 리스, 충돌 revision은 409이며 DB 오류는 전체 rollback됩니다.

## 별도 인스턴스 실행과 원본 보존

다음은 **현재 개발 검증 인스턴스**의 설정입니다. 명령은 상태 조회가 아니라 PostgreSQL 기동, 명시적 스키마 1→2 migration, API 기동을 수행합니다. 소유 상태가 일치하는 실행 중 API는 재사용합니다.

```powershell
pwsh -NoProfile -File .\Tools\Start-Backend.ps1 `
  -ContainerName fablewood-inventory-20261023-postgres `
  -VolumeName fablewood-inventory-20261023-data `
  -ComposeProject fablewood-inventory-20261023 `
  -DatabasePort 55439 -ApiPort 5291 `
  -Schema fable_inventory_20261023 -ServerId inventory-dedicated-20261023
```

이 인스턴스의 API는 `http://127.0.0.1:5291`, PostgreSQL은 `127.0.0.1:55439`입니다. 기존 원본의 `5281/55438`, 컨테이너 `fablewood-postgres`, 볼륨 `fablewood-postgres-data`는 그대로 유지합니다. 실행기의 기본값은 여전히 기존 이름과 포트이므로 개발 사본의 별도 인스턴스에는 위 이름·포트·schema를 명시합니다. 원본 API를 종료하거나 기존 DB를 이 개발 사본으로 자동 이전하지 않습니다.

새 환경의 비밀번호·서버 키·UE 설정·소유권 상태는 해당 백엔드의 `Saved/Backend/`에 생성합니다. 현재 검증 인스턴스에는 그 환경의 비공개 설정이 있으며 소스에는 포함하지 않습니다. 기존 볼륨이 있는데 대응하는 자격 증명이 없거나 소유권·포트·schema가 다르면 초기화나 비밀번호 교체 없이 거절합니다. 다른 루트의 상태 파일을 복사해 재시작하지 않습니다. `-Restart`는 소유 경로·실행 파일·PID·시작 시각이 확인된 API에만 적용하고 DB 볼륨은 유지합니다.

NTFS에서는 비공개 폴더 접근 권한을 제한하며 exFAT에는 사용자별 ACL이 없습니다. 비밀번호·연결 문자열·서버 키는 명령줄·소스·공개 로그에 넣지 않습니다. 환경 변수는 `FABLE_DB_CONNECTION`, `FABLE_DB_SCHEMA`, `FABLE_SERVER_KEY_FILE`이며 직접 키 `FABLE_SERVER_KEY`와 파일 설정을 동시에 지정하지 않습니다. 원격 API는 HTTPS가 필수입니다. `/health`는 DB 연결과 스키마 2를 확인하고, 입장권은 180초·단일 사용, writer 리스는 60초입니다.

## Unreal 연동

별도 백엔드가 준비된 후 형제 게임의 `Start-OnlineServer.cmd -BackendRoot ..\CatPjt-Fablewood-Backend -Port 7802`를 사용합니다. 실행기는 지정한 백엔드의 상태에서 기존 포트·schema·컨테이너·볼륨을 읽고 `<BackendRoot>/Saved/Backend/server.json`을 자동 선택합니다. `Start-OnlineServer.ps1`에는 `BackendConfig` 인자가 없습니다. 클라이언트는 `Play-Online.cmd -Address 127.0.0.1:7802 -BackendUrl http://127.0.0.1:5291`로 연결합니다.

검증기 `Tools/Test-Dedicated.ps1`만 `BackendConfig`를 별도로 지정할 수 있습니다. 검증기는 UE 시작 전에 BackendRoot의 상태·비공개 구성·API/DB 설정 일치를 확인합니다. legacy 복원 검사는 실제 두 계정의 소유자·장비 버전 0/null·원본 프로필 해시를 먼저 검증하여 이미 이전된 계정의 재사용을 거절합니다. 키가 포함된 `server.json`을 게임 소스에 복사하지 않습니다.

## 소스 목록과 검사 범위

```powershell
pwsh -NoProfile -File .\Tools\Get-SourceManifest.ps1
pwsh -NoProfile -File .\Tools\Get-SourceManifest.ps1 -OutputPath '<새 감사 파일의 절대 경로>.json'
```

API·통합 검사·Tools·지정 루트 파일을 포함하며 신규 InventoryContracts/InventoryStore/SQL 002/InventoryTests, 두 lock 파일, SDK 고정, 실행기까지 필수 목록으로 확인합니다. 출력 직전 전체 SHA를 재확인하고 기존 출력은 덮어쓰지 않습니다. `Saved`, `bin/obj`, `artifacts`, DB 데이터·볼륨, 자격 증명·키·상태·로그는 제외합니다. 형제 게임의 `Content` 바이너리 에셋과 `ArtSource` 원본 OBJ 등은 필수 소스 입력이고, 게임 `Saved/Builds/Binaries/Intermediate`는 생성 결과이므로 별도 게임 매니페스트가 구분합니다.

현재 두 저장소의 소스·필수 자산 선택 집합은 308개이며 비밀 설정과 실행 산출물은 포함하지 않습니다. 이 검사는 알려진 비밀 값·일반 설정 패턴과 파일 목록을 확인하는 것이며 모든 비밀의 부재, 빌드, 실제 게임 플레이를 보증하지 않습니다. 실제 통합 검사는 고유 테스트 스키마와 증거를 보존하고 자동 삭제하지 않습니다. PostgreSQL 재시작은 운영자가 별도로 수행하며 검사 명령 자체가 DB를 재시작하지 않습니다.
