# CatPjt-Fablewood-Backend

Fablewood의 계정·접속 티켓·중앙 저장 API입니다. ASP.NET Core 10, Npgsql 10.0.3, PostgreSQL 18을 사용합니다. 게임 전투·이동·거래 가격·보상은 형제 `CatPjt-Fablewood-Game`의 Unreal 전용 서버가 판정하고, API는 프로필·재화·장비 소유권과 변경 이력을 원자적으로 저장합니다.

Fablewood のアカウント・接続チケット・中央保存 API です。ASP.NET Core 10、Npgsql 10.0.3、PostgreSQL 18 を使用します。ゲームの戦闘・移動・取引価格・報酬は兄弟リポジトリ `CatPjt-Fablewood-Game` の Unreal 専用サーバーが判定し、API はプロフィール・通貨等・装備の所有権と変更履歴を原子的に保存します。

현재 경로는 장비 중앙 저장 확장을 위한 별도 개발 사본 `D:\GameProjects\Fablewood-Inventory-20261023\CatPjt-Fablewood-Backend`입니다. 원본 `D:\GameProjects\Fablewood`와 Git 등록 중인 `CatPjt-Fablewood-*` 저장소, 기존 API/PostgreSQL과 계정·비밀 설정은 보존합니다. 초기 분리 이력은 형제 게임의 [DevelopmentHistory.md](https://github.com/krumichan/CatPjt-Fablewood-Game/blob/main/Docs/DevelopmentHistory.md)에 남아 있습니다.

現在のパスは、装備中央保存拡張のための別開発用コピー `D:\GameProjects\Fablewood-Inventory-20261023\CatPjt-Fablewood-Backend` です。原本 `D:\GameProjects\Fablewood` と Git 登録中の `CatPjt-Fablewood-*` リポジトリ、既存の API/PostgreSQL、アカウント・秘密設定は保全します。初期分離の履歴は兄弟ゲームの [DevelopmentHistory.md](https://github.com/krumichan/CatPjt-Fablewood-Game/blob/main/Docs/DevelopmentHistory.md) に残しています。

## 현재 검증 상태 / 現在の検証状況

- API·통합 검사 프로젝트 잠금 복원 빌드: 경고 0개·오류 0개.
- 실제 HTTP/PostgreSQL 통합 검사: 316개 통과(기존 65 + 장비 확장 251).
- 새 별도 PostgreSQL 재시작 후 지속성 검사: 23개 통과.
- 형제 게임의 Editor 빌드와 native 검사 18개 통과.

- API・統合検査プロジェクトのロックファイルに従う復元とビルド: 警告 0 件・エラー 0 件。
- 実際の HTTP/PostgreSQL 統合検査: 316 件通過（既存 65 + 装備拡張 251）。
- 新しい別インスタンスの PostgreSQL 再起動後の永続性検査: 23 件通過。
- 兄弟ゲームの Editor ビルドと native 検査 18 件通過。

실제 UE 검사도 별도로 통과했습니다. Editor-Cmd 전용 서버 1개·Editor 게임 클라이언트 2개가 판매·수리·강화·창고 입출고와 fresh/reconnect/resume을 완료했습니다. 원본의 실제 중앙 저장 v8 복사본 두 개는 새 계정에서 v0/null 상태와 원래 바이트 해시를 확인한 뒤, UUID 백필·v9 이전·재접속·API/서버 재시작 후 기존 재화와 장비 값을 보존했습니다. API 계약 fixture 검사와 실제 UE 이전 검사를 구분합니다. 새 패키지·독립 Server Target·Linux·외부망은 이번 검증 범위 밖입니다.

実際の UE 検査も別途通過しました。Editor-Cmd 専用サーバー 1 つ・Editor ゲームクライアント 2 つが、売却・修理・強化・倉庫への預け入れと取り出し、および fresh/reconnect/resume を完了しました。原本の実際の中央保存 v8 のコピー 2 件は、新規アカウントで v0/null 状態と元のバイトハッシュを確認した後、UUID バックフィル・v9 移行・再接続・API/サーバー再起動を経ても既存の通貨等と装備値を保持しました。API 契約の fixture 検査と実際の UE 移行検査は区別します。新しいパッケージ・独立 Server Target・Linux・外部ネットワークは今回の検証範囲外です。

## 구성과 빌드 / 構成とビルド

- `Fablewood.Backend/`: API, SQL migration 1·2, 패키지 lock, [상세 API 계약](Fablewood.Backend/README.md).
- `Fablewood.Backend.IntegrationTests/`: 실제 HTTP/PostgreSQL 검사. 실행 때 고유 `fable_test_<uuid>` 스키마를 사용합니다.
- `Tools/Start-Backend.ps1`, `Start-Backend.cmd`: Windows 실행기.
- `compose.yaml`, `.env.example`: PostgreSQL 정의와 비밀 없는 예시.
- `global.json`: .NET SDK `10.0.401`, `latestPatch`, 미리 보기 제외.

- `Fablewood.Backend/`: API、SQL migration 1・2、パッケージ lock、[詳細 API 契約](Fablewood.Backend/README.md)。
- `Fablewood.Backend.IntegrationTests/`: 実際の HTTP/PostgreSQL 検査。実行時に固有の `fable_test_<uuid>` スキーマを使用します。
- `Tools/Start-Backend.ps1`、`Start-Backend.cmd`: Windows ランチャー。
- `compose.yaml`、`.env.example`: PostgreSQL 定義と秘密値を含まない例。
- `global.json`: .NET SDK `10.0.401`、`latestPatch`、プレビュー版は除外。

이 폴더에서 .NET SDK와 PowerShell 7.4 이상을 사용합니다. lock 파일을 갱신하지 않는 빌드 명령입니다.

このフォルダーで .NET SDK と PowerShell 7.4 以上を使用します。以下は lock ファイルを更新しないビルドコマンドです。

```powershell
dotnet restore .\Fablewood.Backend\Fablewood.Backend.csproj --locked-mode
dotnet restore .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj --locked-mode
dotnet build .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj -c Release --no-restore
```

상대 프로젝트 참조로 API도 함께 빌드됩니다. 출력은 각 프로젝트의 `bin/Release/net10.0/`이며 실행기는 자동 빌드하지 않습니다. 현재 개발 검증에서 생성한 `bin/obj`·실행 상태가 있더라도 소스 배포에는 포함하지 않습니다.

相対プロジェクト参照によって API も同時にビルドされます。出力先は各プロジェクトの `bin/Release/net10.0/` で、ランチャーは自動ビルドしません。現在の開発検証で生成した `bin/obj`・実行状態が存在しても、ソース配布には含めません。

## 장비 저장 계약 / 装備保存の契約

DB 스키마는 2, 정형 `inventoryVersion`은 1, 새 UE SaveGame은 v9입니다. 아이템 UUID·소유자·기존 로컬 ID·가방/창고 위치·장착 참조와 옵션·품질·등급·강화·내구도를 정형 테이블에 저장합니다. `POST /v1/server/commit-inventory`는 profile 행 잠금, 단일 writer 리스, expected revision, 작업 ID/전체 요청 해시를 확인하고 blob·재화 7종·장비/옵션·receipt·경제 ledger·장비 journal을 한 트랜잭션에서 확정합니다.

DB スキーマは 2、構造化された `inventoryVersion` は 1、新しい UE SaveGame は v9 です。アイテム UUID・所有者・既存ローカル ID・バッグ/倉庫内の位置・装着参照と、オプション・品質・レアリティ・強化・耐久度を構造化テーブルに保存します。`POST /v1/server/commit-inventory` は profile 行ロック、単一 writer リース、expected revision、操作 ID/リクエスト全体のハッシュを確認し、blob・7 種類の通貨/素材/消耗品・装備/オプション・receipt・経済 ledger・装備 journal を 1 トランザクションで確定します。

UUID 소유자는 서버 인증과 리스로 확인한 플레이어이며 클라이언트가 지정하지 않습니다. 제거된 아이템은 소유 이력을 남기고 UUID·로컬 ID 재사용을 거절합니다. 슬롯·직업·품질·등급·옵션·내구도 범위와 장착 위치도 검증합니다. `GET /v1/me/inventory`는 Bearer 계정 자신의 목록만 반환합니다. 게임 가격·생성/보상 조건은 UE 권위이며 API가 다시 계산하지 않습니다.

UUID の所有者はサーバー認証とリースで確認したプレイヤーであり、クライアントが指定するものではありません。除去されたアイテムも所有履歴を残し、UUID・ローカル ID の再利用を拒否します。スロット・クラス・品質・レアリティ・オプション・耐久度の範囲と装着位置も検証します。`GET /v1/me/inventory` は Bearer アカウント本人の一覧だけを返します。ゲームの価格・生成/報酬条件の判定権限は UE にあり、API は再計算しません。

아직 이전하지 않은 중앙 프로필은 `inventoryVersion:0, inventory:null`입니다. UE가 기존 v8 이하 blob을 먼저 검증한 후 원래 장비 값·옵션·강화·내구도·로컬 ID·배열 순서를 유지하며 누락 UUID를 부여하고 첫 정형 백필 ACK 전에는 게임을 시작하지 않습니다. 이후 claim에서는 원본 v9 blob과 정형 목록의 전체 필드/순서/장착 참조를 대조하고 불일치를 덮어쓰지 않습니다. API는 Unreal blob을 해독하지 않으며 이 의미적 대조는 UE가 수행합니다. 로컬 `.sav` 자동 업로드는 없습니다.

未移行の中央プロフィールは `inventoryVersion:0, inventory:null` です。UE は既存の v8 以下の blob を先に検証し、元の装備値・オプション・強化・耐久度・ローカル ID・配列順序を維持して欠けている UUID を付与します。最初の構造化バックフィル ACK まではゲームを開始しません。以降の claim では原本 v9 blob と構造化一覧の全フィールド/順序/装着参照を照合し、不一致を上書きしません。API は Unreal blob を解読せず、この意味的な照合は UE が行います。ローカル `.sav` の自動アップロードはありません。

기존 `/v1/server/commit`와 그 요청 해시는 유지합니다. 정형 이전 후에는 새 legacy mutation을 거절하되 이미 성공한 동일 작업 receipt의 읽기 전용 replay는 보존합니다. 같은 ID에 다른 payload, 이전 리스, 충돌 revision은 409이며 DB 오류는 전체 rollback됩니다.

既存の `/v1/server/commit` とそのリクエストハッシュを維持します。構造化移行後は新しい legacy mutation を拒否しますが、成功済みの同一操作 receipt の読み取り専用 replay は保全します。同じ ID に異なる payload、以前のリース、衝突する revision は 409 となり、DB エラーでは全体を rollback します。

## 별도 인스턴스 실행과 원본 보존 / 別インスタンスの起動と原本の保全

다음은 **현재 개발 검증 인스턴스**의 설정입니다. 명령은 상태 조회가 아니라 PostgreSQL 기동, 명시적 스키마 1→2 migration, API 기동을 수행합니다. 소유 상태가 일치하는 실행 중 API는 재사용합니다.

以下は**現在の開発検証インスタンス**の設定です。このコマンドは状態照会ではなく、PostgreSQL の起動、明示的なスキーマ 1→2 migration、API の起動を実行します。所有状態が一致する起動中の API は再利用します。

```powershell
pwsh -NoProfile -File .\Tools\Start-Backend.ps1 `
  -ContainerName fablewood-inventory-20261023-postgres `
  -VolumeName fablewood-inventory-20261023-data `
  -ComposeProject fablewood-inventory-20261023 `
  -DatabasePort 55439 -ApiPort 5291 `
  -Schema fable_inventory_20261023 -ServerId inventory-dedicated-20261023
```

이 인스턴스의 API는 `http://127.0.0.1:5291`, PostgreSQL은 `127.0.0.1:55439`입니다. 기존 원본의 `5281/55438`, 컨테이너 `fablewood-postgres`, 볼륨 `fablewood-postgres-data`는 그대로 유지합니다. 실행기의 기본값은 여전히 기존 이름과 포트이므로 개발 사본의 별도 인스턴스에는 위 이름·포트·schema를 명시합니다. 원본 API를 종료하거나 기존 DB를 이 개발 사본으로 자동 이전하지 않습니다.

このインスタンスの API は `http://127.0.0.1:5291`、PostgreSQL は `127.0.0.1:55439` です。既存の原本の `5281/55438`、コンテナー `fablewood-postgres`、ボリューム `fablewood-postgres-data` は維持します。ランチャーの既定値は引き続き従来の名前とポートなので、開発用コピーの別インスタンスには上記の名前・ポート・schema を明示します。原本 API の終了や、既存 DB のこの開発用コピーへの自動移行は行いません。

새 환경의 비밀번호·서버 키·UE 설정·소유권 상태는 해당 백엔드의 `Saved/Backend/`에 생성합니다. 현재 검증 인스턴스에는 그 환경의 비공개 설정이 있으며 소스에는 포함하지 않습니다. 기존 볼륨이 있는데 대응하는 자격 증명이 없거나 소유권·포트·schema가 다르면 초기화나 비밀번호 교체 없이 거절합니다. 다른 루트의 상태 파일을 복사해 재시작하지 않습니다. `-Restart`는 소유 경로·실행 파일·PID·시작 시각이 확인된 API에만 적용하고 DB 볼륨은 유지합니다.

新環境のパスワード・サーバーキー・UE 設定・所有状態は、そのバックエンドの `Saved/Backend/` に生成します。現在の検証インスタンスにはその環境の非公開設定があり、ソースには含めません。既存ボリュームがあるのに対応する資格情報がない場合や、所有権・ポート・schema が異なる場合は、初期化やパスワード交換をせず拒否します。別ルートの状態ファイルをコピーして再起動しません。`-Restart` は所有パス・実行ファイル・PID・開始時刻を確認できた API にのみ適用し、DB ボリュームは維持します。

NTFS에서는 비공개 폴더 접근 권한을 제한하며 exFAT에는 사용자별 ACL이 없습니다. 비밀번호·연결 문자열·서버 키는 명령줄·소스·공개 로그에 넣지 않습니다. 환경 변수는 `FABLE_DB_CONNECTION`, `FABLE_DB_SCHEMA`, `FABLE_SERVER_KEY_FILE`이며 직접 키 `FABLE_SERVER_KEY`와 파일 설정을 동시에 지정하지 않습니다. 원격 API는 HTTPS가 필수입니다. `/health`는 DB 연결과 스키마 2를 확인하고, 입장권은 180초·단일 사용, writer 리스는 60초입니다.

NTFS では非公開フォルダーのアクセス権を制限しますが、exFAT にはユーザー別 ACL がありません。パスワード・接続文字列・サーバーキーはコマンドライン・ソース・公開ログに含めません。環境変数は `FABLE_DB_CONNECTION`、`FABLE_DB_SCHEMA`、`FABLE_SERVER_KEY_FILE` で、直接キー `FABLE_SERVER_KEY` とファイル設定を同時に指定しません。リモート API には HTTPS が必須です。`/health` は DB 接続とスキーマ 2 を確認し、入場券は 180 秒・1 回限り、writer リースは 60 秒です。

## Unreal 연동 / Unreal との連携

별도 백엔드가 준비된 후 형제 게임의 `Start-OnlineServer.cmd -BackendRoot ..\CatPjt-Fablewood-Backend -Port 7802`를 사용합니다. 실행기는 지정한 백엔드의 상태에서 기존 포트·schema·컨테이너·볼륨을 읽고 `<BackendRoot>/Saved/Backend/server.json`을 자동 선택합니다. `Start-OnlineServer.ps1`에는 `BackendConfig` 인자가 없습니다. 클라이언트는 `Play-Online.cmd -Address 127.0.0.1:7802 -BackendUrl http://127.0.0.1:5291`로 연결합니다.

別バックエンドの準備後、兄弟ゲームの `Start-OnlineServer.cmd -BackendRoot ..\CatPjt-Fablewood-Backend -Port 7802` を使用します。ランチャーは指定バックエンドの状態から既存のポート・schema・コンテナー・ボリュームを読み、`<BackendRoot>/Saved/Backend/server.json` を自動選択します。`Start-OnlineServer.ps1` に `BackendConfig` 引数はありません。クライアントは `Play-Online.cmd -Address 127.0.0.1:7802 -BackendUrl http://127.0.0.1:5291` で接続します。

검증기 `Tools/Test-Dedicated.ps1`만 `BackendConfig`를 별도로 지정할 수 있습니다. 검증기는 UE 시작 전에 BackendRoot의 상태·비공개 구성·API/DB 설정 일치를 확인합니다. legacy 복원 검사는 실제 두 계정의 소유자·장비 버전 0/null·원본 프로필 해시를 먼저 검증하여 이미 이전된 계정의 재사용을 거절합니다. 키가 포함된 `server.json`을 게임 소스에 복사하지 않습니다.

検証ツール `Tools/Test-Dedicated.ps1` だけが `BackendConfig` を個別指定できます。検証ツールは UE 起動前に BackendRoot の状態・非公開構成・API/DB 設定の一致を確認します。legacy 復元検査は、実際の 2 アカウントの所有者・装備バージョン 0/null・原本プロフィールハッシュを先に検証し、移行済みアカウントの再利用を拒否します。キーを含む `server.json` をゲームソースにコピーしません。

## 소스 목록과 검사 범위 / ソース一覧と検査範囲

```powershell
pwsh -NoProfile -File .\Tools\Get-SourceManifest.ps1
pwsh -NoProfile -File .\Tools\Get-SourceManifest.ps1 -OutputPath '<새 감사 파일의 절대 경로>.json'
```

上の `-OutputPath` の `<새 감사 파일의 절대 경로>` は「新しい監査ファイルの絶対パス」に置き換えます。

API·통합 검사·Tools·지정 루트 파일을 포함하며 신규 InventoryContracts/InventoryStore/SQL 002/InventoryTests, 두 lock 파일, SDK 고정, 실행기까지 필수 목록으로 확인합니다. 출력 직전 전체 SHA를 재확인하고 기존 출력은 덮어쓰지 않습니다. `Saved`, `bin/obj`, `artifacts`, DB 데이터·볼륨, 자격 증명·키·상태·로그는 제외합니다. 형제 게임의 `Content` 바이너리 에셋과 `ArtSource` 원본 OBJ 등은 필수 소스 입력이고, 게임 `Saved/Builds/Binaries/Intermediate`는 생성 결과이므로 별도 게임 매니페스트가 구분합니다.

API・統合検査・Tools・指定ルートファイルを含め、新しい InventoryContracts/InventoryStore/SQL 002/InventoryTests、2 つの lock ファイル、SDK 固定、ランチャーまで必須一覧として確認します。出力直前に全 SHA を再確認し、既存の出力は上書きしません。`Saved`、`bin/obj`、`artifacts`、DB データ・ボリューム、資格情報・キー・状態・ログは除外します。兄弟ゲームの `Content` バイナリアセットと `ArtSource` の OBJ 原本などは必須のソース入力であり、ゲームの `Saved/Builds/Binaries/Intermediate` は生成物なので、別のゲーム manifest が区別します。

현재 두 저장소의 소스·필수 자산 선택 집합은 308개이며 비밀 설정과 실행 산출물은 포함하지 않습니다. 이 검사는 알려진 비밀 값·일반 설정 패턴과 파일 목록을 확인하는 것이며 모든 비밀의 부재, 빌드, 실제 게임 플레이를 보증하지 않습니다. 실제 통합 검사는 고유 테스트 스키마와 증거를 보존하고 자동 삭제하지 않습니다. PostgreSQL 재시작은 운영자가 별도로 수행하며 검사 명령 자체가 DB를 재시작하지 않습니다.

現在の 2 リポジトリのソース・必須アセット選択集合は 308 個で、秘密設定と実行生成物は含めません。この検査は既知の秘密値・一般的な設定パターンとファイル一覧を確認するもので、あらゆる秘密が存在しないこと、ビルド、実際のゲームプレイを保証しません。実際の統合検査では固有のテストスキーマと証拠を保全し、自動削除しません。PostgreSQL の再起動は運用者が別途行い、検査コマンド自体は DB を再起動しません。
