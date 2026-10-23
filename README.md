# CatPjt-Fablewood-Backend

Fablewood의 계정·접속 티켓·중앙 저장 API를 독립 폴더로 분리한 소스입니다. ASP.NET Core 10, Npgsql 10.0.3, PostgreSQL 18을 사용합니다. 게임 전투와 거래 판정은 별도 `CatPjt-Fablewood-Game`의 Unreal 전용 서버가 담당하고, API는 프로필·지갑·재화 이력을 원자적으로 저장합니다.

이번 분리는 소스 복사입니다. 원본 `D:\GameProjects\Fablewood`, 실행 중인 API/PostgreSQL, 기존 계정과 저장 데이터, 비밀 설정은 이전하거나 변경하지 않았습니다. 분리 후 새 경로의 API·통합 검사 프로젝트를 lock 파일 고정 상태로 빌드했고 경고 0개·오류 0개로 성공했습니다. 빌드 출력은 중앙 검증 폴더에만 두었으며 새 저장소에 `bin/obj`를 생성하지 않았습니다. 통합 검사·서비스·게임은 이번 분리 검증에서 재실행하지 않았습니다.

## 구성과 빌드

- `Fablewood.Backend/`: API 소스, SQL migration, 패키지 lock 파일, [API 계약](Fablewood.Backend/README.md).
- `Fablewood.Backend.IntegrationTests/`: 실제 HTTP/PostgreSQL 집중 통합 검사. 실행할 때만 고유 테스트 스키마를 생성합니다.
- `Tools/Start-Backend.ps1`, `Start-Backend.cmd`: Windows 실행기.
- `compose.yaml`, `.env.example`: PostgreSQL 정의와 비밀이 없는 예시.
- `global.json`: SDK `10.0.401`, `latestPatch`, 미리 보기 제외. 프로젝트와 어셈블리 이름은 `Fablewood.Backend`를 유지합니다.

이 폴더를 현재 작업 폴더로 삼고 .NET SDK와 PowerShell 7.4 이상을 사용합니다. lock 파일을 갱신하지 않는 빌드 명령은 다음과 같습니다.

```powershell
dotnet restore .\Fablewood.Backend\Fablewood.Backend.csproj --locked-mode
dotnet restore .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj --locked-mode
dotnet build .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj -c Release --no-restore
```

통합 검사 프로젝트의 상대 참조로 API도 함께 빌드됩니다. 출력은 각각 `bin/Release/net10.0/`이며 실행기는 직접 빌드하지 않습니다. 기존 원본의 바이너리·`bin/obj` 캐시는 이 소스 사본에 포함하지 않습니다.

## 실행과 기존 서비스 보존

서비스 기동이 필요한 때 `Start-Backend.cmd` 또는 아래 명령을 사용합니다. 이 명령은 상태 조회가 아니라 PostgreSQL 기동, 스키마 1 migration, API 기동을 수행합니다.

```powershell
pwsh -NoProfile -File .\Tools\Start-Backend.ps1
```

기본 API는 `127.0.0.1:5281`, PostgreSQL은 `127.0.0.1:55438`입니다. 기존과 같은 Compose 프로젝트 `fablewood-backend`, 컨테이너 `fablewood-postgres`, 볼륨 `fablewood-postgres-data`를 유지했습니다. 볼륨을 삭제하거나 새 비밀번호로 초기화하는 기능은 없습니다.

**현재 새 폴더에는 기존 `Saved/Backend` 비밀 설정이 없습니다.** 먼저 빌드 산출물이 없으면 실행을 거절하며, 빌드 이후에도 기존 볼륨은 있는데 이 폴더의 자격 증명이 없으면 보호를 위해 거절합니다. 기존 서비스의 자격 증명이나 실행 상태를 자동으로 가져오지 않습니다. 원본 상태 파일은 원본 경로·실행 파일·PID·시작 시각에 묶여 있으므로 새 폴더로 복사해서 재시작에 사용하지 마세요. `-Restart`도 새 실행기가 확인한 자기 프로세스에만 적용됩니다.

기존 서비스는 계속 원본 `D:\GameProjects\Fablewood\Tools\Start-Backend.ps1`에서 관리합니다. 새 실행기로 운영 주체를 옮기는 작업은 별도 계획에서 자격 증명·소유권·포트·정상 종료를 확인한 뒤 수행합니다. 이번 분리에는 DB 이전, 비밀번호 재발급, 볼륨 교체, 기존 API 종료·재시작이 포함되지 않습니다.

새 환경을 최초 구성하는 경우에만 실행기가 `Saved/Backend/` 아래에 비밀번호·서버 키·UE 설정·소유권 상태를 생성합니다. 이 디렉터리는 소스에서 제외합니다. NTFS에서는 접근 권한을 제한하고, exFAT에서는 사용자별 ACL이 제공되지 않습니다. 계정 비밀번호, DB 연결 문자열, 서버 키를 명령줄·소스·공개 로그에 넣지 않습니다. 원격 API 통신은 HTTPS를 사용합니다.

API의 환경 변수는 `FABLE_DB_CONNECTION`, `FABLE_DB_SCHEMA`, `FABLE_SERVER_KEY_FILE`입니다. 직접 키 값의 `FABLE_SERVER_KEY`와 파일 설정을 동시에 지정하지 않습니다. `/health`는 DB 연결과 스키마 준비까지 확인합니다. 접속 티켓은 180초·단일 사용이고 서버 저장 리스는 60초입니다.

## Unreal 게임 연동

형제 게임 폴더 `CatPjt-Fablewood-Game`의 `Tools/Start-OnlineServer.ps1`은 `BackendRoot`를 받아 그 루트의 `Saved/Backend/server.json`과 실행 상태를 자동 선택합니다. `Tools/Test-Dedicated.ps1`은 `BackendConfig`로 별도 서버 설정 경로도 명시할 수 있습니다. 이 백엔드가 새 환경에서 정상 구성되면 설정 위치는 `<백엔드 루트>/Saved/Backend/server.json`입니다. 이 파일에는 서버 키가 있으므로 게임 소스에 복사하지 않습니다.

현재 원본 서비스에 연결할 때 온라인 실행기의 `BackendRoot`는 `D:\GameProjects\Fablewood`로 지정합니다. 전용 검증기의 설정 경로를 별도로 지정한다면 `BackendConfig`는 `D:\GameProjects\Fablewood\Saved\Backend\server.json`입니다. 게임·백엔드 폴더의 이름을 다시 정하더라도 API 프로젝트·DB·볼륨 이름까지 함께 변경할 필요는 없습니다.

## 소스 목록과 검사 범위

```powershell
pwsh -NoProfile -File .\Tools\Get-SourceManifest.ps1
pwsh -NoProfile -File .\Tools\Get-SourceManifest.ps1 -OutputPath '<새 감사 파일의 절대 경로>.json'
```

소스 목록은 API·통합 검사·Tools와 지정한 루트 파일의 상대 경로·크기·SHA256을 기록합니다. 필수 소스·두 lock 파일·SDK 고정·migration·실행기를 확인하고, `Saved`, `bin/obj`, DB 데이터, 비밀 설정을 제외합니다. 알려진 로컬 비밀 값 및 설정 파일의 일반적인 비밀 패턴을 검사하며 값 자체는 출력하지 않습니다. 기본 출력은 `Saved/SourceManifest/`의 새 파일이고 기존 출력은 덮어쓰지 않습니다. 이 검사는 빌드나 서비스·게임 테스트를 실행하지 않으며, 모든 형태의 비밀을 식별한다는 보증은 아닙니다.

실제 통합 검사는 API 계약 문서의 명령과 별도 준비된 PostgreSQL을 사용합니다. 기존 제품 스키마 대신 고유 `fable_test_<uuid>` 스키마를 만들며, 검사 스키마와 증거는 자동 삭제하지 않습니다. DB 재시작 검사는 별도 운영자 작업입니다.
