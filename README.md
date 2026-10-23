## 최신 안내 · 2026-10-24 / 最新案内 · 2026-10-24

**최신 Foundation 사용·계약 설명은 [아래 Foundation 개발 기록](#foundation-development-record-20261023)에 있다.** 그 앞의 기존 한일 공개 본문과 아래 Foundation 원문의 상태 문장은 각 단계의 이력으로 보존했다. 23일 원문의 ‘패키지 플레이 미검증’은 당시 상태이며, 아래 1단계 결과가 그 한 항목을 갱신한다. 기존 단계의 사실이나 실패 기록은 삭제하지 않았다.

**最新の Foundation の使い方・契約は[下の Foundation 開発記録](#foundation-development-record-20261023)にあります。** その前の既存韓日公開本文と Foundation 原文の状態記述は、各段階の履歴として保存しています。23 日の「package play 未検証」は当時の状態で、以下の第 1 段階結果がその項目を更新します。旧段階の事実・失敗記録は削除していません。

- 기능·소스 변경일은 **2026-10-23 KST**, 이 일본어 번역·공개 문서 병합과 최종 안내 작성일은 **2026-10-24 KST**다. 1단계 실제 검증은 23일 23:56경 시작해 **24일 00:00:15 KST**에 끝났고, 최종 정적 감사·화면 검토는 24일에 수행됐다. 날짜를 파일 수정 시각으로 추정하지 않았다.
- 실제 작업 **`20261023T145628442Z-97b1128f` · Exit 0**에서 Windows 패키지 클라이언트 두 개와 **Editor 전용 서버**의 가입·로그인, 같은 DB를 유지한 API 재시작 뒤 캐릭터 지갑·전체 아이템·옵션·장착·요약의 영속 복원을 확인했다. 초기·복원 실행의 실제 패키지 클라이언트 프로세스는 합계 4개이며, 동시 클라이언트 4명 검증이라는 뜻이 아니다.
- 이 작업은 한 Windows PC의 loopback 검증이다. **2~4단계의 구현·검증, 전체 던전·장비 성장·던전 중단 복구, Linux, 서로 다른 물리 PC 두 대, 외부망, 독립 Server Target 실행 파일은 완료로 표시하지 않는다.** 서버는 `UnrealEditor-Cmd.exe -server -NullRHI`였으며 `FablewoodServer.exe`를 빌드·시험한 결과가 아니다.
- Foundation의 HTTP/PostgreSQL **556개**, 실제 DB 재시작 지속성 **42개**, native **22개**, Editor 빌드·Windows Game 패키징은 각각 앞선 별도 작업의 결과다. 이번 패키지 클라이언트 1단계에서 전부 재실행했다고 하거나 통과 수를 합산하지 않는다.

- 機能・source の変更日は **2026-10-23 KST**、今回の日本語翻訳・公開文書の統合・最新案内の作成日は **2026-10-24 KST** です。第 1 段階の実検証は 23 日 23:56 頃に始まり、**24 日 00:00:15 KST** に終了しました。最終静的監査・画面レビューは 24 日です。file 更新時刻から日付を推測していません。
- 実 job **`20261023T145628442Z-97b1128f`・Exit 0** で Windows package client 2 個と **Editor 専用 server** による登録・login、同じ DB を保つ API 再起動後の character wallet・全 item・affix・equipment・summary の永続復元を確認しました。初回/復元実行の実 package client process は合計 4 個であり、4 人同時 client 検証ではありません。
- 1 台の Windows PC の loopback 検証です。**第 2～4 段階の実装・検証、全 dungeon・装備成長・dungeon 中断復旧、Linux、別の物理 PC 2 台、外部 network、独立 Server Target 実行ファイルを完了扱いしません。** server は `UnrealEditor-Cmd.exe -server -NullRHI` であり、`FablewoodServer.exe` を build・test した結果ではありません。
- Foundation の HTTP/PostgreSQL **556 件**、実 DB 再起動永続性 **42 件**、native **22 件**、Editor build・Windows Game packaging は、それぞれ先行する別 job の結果です。今回の package client 第 1 段階ですべて再実行したとは記録せず、件数も合算しません。

소스 대조의 한계도 보존한다. 원시 보고서의 `reusedBuild.compiledAndCookedInputsMatched=0`은 역슬래시/경로 선택 버그이며 성공 근거로 사용하지 않았다. 별도 정규화 보완 대조는 고정 입력 324개 중 320개 동일·승인 차이 4개와 컴파일/cook 입력 260개 일치를 확인했다. 실제 runtime은 재사용 Editor 바이너리 3개와 패키지 사본 2개를 별도로 확인했다. 보완 정적 대조는 옛 데스크톱 소스 파일을 다시 읽거나 게임 플레이를 재실행한 검사가 아니다. 원시 보고서를 덮어쓰지 않았다.

source 照合の限界も保存します。原 report の `reusedBuild.compiledAndCookedInputsMatched=0` は backslash/path selector の bug で、成功根拠には使っていません。別の正規化補足照合で固定入力 324 件中 320 件一致・承認差分 4 件、compiled/cooked 入力 260 件の一致を確認しました。実 runtime は再利用 Editor binary 3 個と package copy 2 個を別途確認しました。補足静的照合は旧 desktop source file の再読や gameplay 再実行ではなく、原 report は上書きしていません。

근거는 비공개 로컬 검증 기록 `stage1-git-handoff.json`, `packaged-two-client-validation.json`, `stage1-source-provenance.json`, `stage1-visual-review.json`, `reused-source-supplement-static.json`이다. 이 문서 반영은 새 게임 실행·DB 변경·서비스 배포를 수행하거나 증빙·로그·캡처를 공개 저장소에 추가한 작업이 아니다.

根拠は非公開のローカル検証記録 `stage1-git-handoff.json`、`packaged-two-client-validation.json`、`stage1-source-provenance.json`、`stage1-visual-review.json`、`reused-source-supplement-static.json` です。この文書反映で新たな game 実行・DB 変更・service deploy は行わず、根拠・log・capture を公開 repository に追加していません。

---

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


<a id="foundation-development-record-20261023"></a>

## Foundation 개발 원문 · 2026-10-23 / Foundation 開発原文 · 2026-10-23

이 구역은 23일 Foundation 문서를 원문 단위로 보존한 한일 대응이다. 당시 ‘현재’와 미검증 상태는 이력이며, 위 24일 안내가 최신 1단계 결과를 구분한다.

本節は 23 日の Foundation 文書を原文単位で保存した韓日対応です。当時の「現在」・未検証状態は履歴で、上の 24 日案内が最新の第 1 段階結果を区別します。

# CatPjt-Fablewood-Backend

# CatPjt-Fablewood-Backend

Fablewood의 계정·접속권·캐릭터·중앙 저장 API입니다. ASP.NET Core 10, Npgsql 10.0.3, PostgreSQL 18을 사용합니다. 형제 `CatPjt-Fablewood-Game`의 Unreal 전용 서버가 이동·전투·보상·거래를 판정하고, API가 소유권·세션·저장 트랜잭션을 검증합니다.

Fablewood の account・接続 ticket・character・中央保存 API です。ASP.NET Core 10、Npgsql 10.0.3、PostgreSQL 18 を使います。兄弟 `CatPjt-Fablewood-Game` の Unreal 専用 server が移動・戦闘・報酬・取引を判定し、API が ownership・session・保存 transaction を検証します。

이번 작업 경로는 `D:\GameProjects\Fablewood-OnlineFlow-20261023\CatPjt-Fablewood-Backend`입니다. 기존 원본, Git 등록 중인 저장소와 완료된 Inventory 작업본·서비스·데이터는 보존합니다.

今回の作業 path は `D:\GameProjects\Fablewood-OnlineFlow-20261023\CatPjt-Fablewood-Backend` です。既存原本、Git 登録中 repository、完了済み Inventory 作業コピー・service・data は保存します。

## 현재 상태

## 当時の状態

2026-10-23 공용 데스크톱의 .NET SDK 10.0.401로 API와 통합 검사 프로젝트를 잠금 복원 Release 빌드했고 경고 0개·오류 0개로 통과했습니다. 스키마 3 HTTP/PostgreSQL 556개와 실제 PostgreSQL 재시작 후 영구 저장 42개는 `20261023T082744298Z-adce3e5f`에서 통과했습니다. 해당 API·SQL·검사 코드는 유지했고, 이후 수정한 Windows ACL 런처는 `20261023T131351827Z-3d0a125a`의 실제 5개 사례와 최종 온라인 작업으로 검증했습니다. `20261023T131703552Z-fa59464f`에서 Unreal Editor 전용 서버 1개·렌더링 클라이언트 2개가 같은 DB의 API 재시작 전후 동일 캐릭터·재화·장비·아이템·요약으로 재로그인했습니다. 한 PC loopback 결과이며 독립 Server Target·Linux·외부망은 미검증입니다.

2026-10-23 に共用 desktop の .NET SDK 10.0.401 で API と統合検査 project を lockfile に従って restore し、Release build は警告 0・error 0 で通過しました。schema 3 HTTP/PostgreSQL 556 件と実 PostgreSQL 再起動後の永続保存 42 件は `20261023T082744298Z-adce3e5f` で通過しました。その API・SQL・検査コードを維持し、その後変更した Windows ACL launcher は `20261023T131351827Z-3d0a125a` の実 5 case と当時の最終 online job で検証しました。`20261023T131703552Z-fa59464f` では Unreal Editor 専用 server 1 個・描画 client 2 個が、同じ DB の API 再起動前後で同一 character・通貨等・装備・item・summary に再 login しました。1 PC loopback の結果で、独立 Server Target・Linux・外部 network は未検証です。

이전 Inventory 단계에서 통과한 통합 316개·DB 재시작 지속성 23개·native 18개·실제 2인 장비 검사는 이전 소스의 결과입니다. 이번 캐릭터·세션·안전 퇴장 확장의 통과 근거로 재사용하지 않습니다. 새 회귀 검사는 소스에 포함되어 있습니다.

以前の Inventory 段階で通過した統合 316 件・DB 再起動永続性 23 件・native 18 件・実 2 人装備検査は以前の source の結果です。今回の character・session・安全退場拡張の通過根拠として再利用しません。新しい回帰検査は source に含まれます。

## 구성과 빌드

## 構成と build

- `Fablewood.Backend/`: API, SQL migration 001~003, package lock, [API 계약](https://github.com/krumichan/CatPjt-Fablewood-Backend/blob/main/Fablewood.Backend/README.md).
- `Fablewood.Backend.IntegrationTests/`: 실제 HTTP/PostgreSQL 검사, 고유 테스트 스키마 사용.
- `Tools/Start-Backend.ps1`, `Start-Backend.cmd`: Windows의 소유권 확인 실행기.
- `compose.yaml`, `.env.example`: PostgreSQL 정의와 비밀 없는 예시.
- `global.json`: .NET SDK 10.0.401, latestPatch, 미리 보기 제외.

- `Fablewood.Backend/`：API、SQL migration 001～003、package lock、[API 契約](https://github.com/krumichan/CatPjt-Fablewood-Backend/blob/main/Fablewood.Backend/README.md)。
- `Fablewood.Backend.IntegrationTests/`：実 HTTP/PostgreSQL 検査。固有 test schema を使用。
- `Tools/Start-Backend.ps1`、`Start-Backend.cmd`：Windows の所有関係確認 launcher。
- `compose.yaml`、`.env.example`：PostgreSQL 定義と秘密情報なしの例。
- `global.json`：.NET SDK 10.0.401、latestPatch、preview 除外。

PowerShell 7.4 이상과 SDK를 준비한 데스크톱에서 아래 명령을 사용합니다. lock 파일을 변경하지 않습니다.

PowerShell 7.4 以上と SDK を準備した desktop で以下を使います。lockfile は変更しません。

```powershell
dotnet restore .\Fablewood.Backend\Fablewood.Backend.csproj --locked-mode
dotnet restore .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj --locked-mode
dotnet build .\Fablewood.Backend.IntegrationTests\Fablewood.Backend.IntegrationTests.csproj -c Release --no-restore
```

통합 검사 프로젝트가 API를 참조합니다. 실행기는 `bin/Release/net10.0` 빌드를 요구하고 자동 빌드하지 않습니다. 모든 `bin/obj`, 로그와 상태는 소스 배포에서 제외합니다.

統合検査 project は API を参照します。launcher は `bin/Release/net10.0` build を要求し、自動 build しません。全 `bin/obj`、log、state は source 配布から除外します。

## 캐릭터·세션·저장

## Character・session・保存

DB 스키마는 **3**, UE SaveGame은 v9, 정형 장비·캐릭터 요약은 각각 버전 1입니다. `GET /v1/me/character`는 로그인한 계정 하나의 이름·직업·레벨·전직·재화·장비 미리보기를 반환합니다. 기존 프로필에 요약이 없으면 null을 유지하고 UE가 첫 접속 시 실제 SaveGame을 검증해 백필합니다. API가 Unreal blob을 해독하거나 임의의 기존 레벨을 생성하지 않습니다.

DB schema は **3**、UE SaveGame は v9、構造化装備・character summary は各 version 1。`GET /v1/me/character` は login account 1 個の名前・class・level・class tier・通貨等・装備 preview を返します。既存 profile に summary がなければ null のままとし、UE が初回接続時に実 SaveGame を検証して backfill します。API は Unreal blob を解読せず、既存 level を勝手に生成しません。

`POST /v1/server/commit-character`는 blob·재화 7종·전체 장비/옵션·요약·receipt·ledger·journal을 한 트랜잭션으로 확정합니다. 서버 인증, 소유 계정의 writer 리스, expected revision과 요청 해시를 확인합니다. 같은 성공 작업은 재시도할 수 있지만 다른 payload·이전 리스·revision 충돌은 거절합니다. 이전 commit 경로의 과거 receipt 조회도 보존합니다. 신규 요약 저장 이후 구버전 API로 새 변형을 저장하지 않습니다.

`POST /v1/server/commit-character` は blob・通貨等 7 種・全装備/affix・summary・receipt・ledger・journal を 1 transaction で確定します。server 認証、所有 account の writer lease、expected revision と request hash を検査します。同じ成功操作は retry できますが、別 payload・旧 lease・revision 競合は拒否します。旧 commit route の過去 receipt 読み取りも維持します。新 summary 保存後に旧版 API で新 mutation を保存しません。

`GET /v1/servers`는 설정된 서버 목록이며 health ping이 아닙니다. `POST /v1/sessions/logout`은 현재 세션과 그 세션의 미사용 접속권을 취소합니다. 이미 claim한 writer 리스는 저장 완료까지 유지합니다. `POST /v1/server/release-confirmed`는 반환 receipt를 남겨 성공 응답 유실 후 재시도할 수 있고, 새 접속의 리스를 해제하지 않습니다.

`GET /v1/servers` は設定された server list であり health ping ではありません。`POST /v1/sessions/logout` は現在 session とその未使用 ticket を取り消します。claim 済み writer lease は保存完了まで維持します。`POST /v1/server/release-confirmed` は返却 receipt を残して成功応答喪失後の retry を可能にし、新接続の lease は解放しません。

아이템 UUID·소유자·기존 로컬 ID·가방/창고 순서·장착·품질·등급·옵션·강화·내구도는 중앙 저장합니다. 삭제된 ID 이력도 보존합니다. 전투·가격·보상 판정은 UE 권위이며, 로컬 `.sav` 자동 업로드는 없습니다. 자세한 DTO·오류·이전 규칙은 API 계약 문서를 따릅니다.

item UUID・owner・既存 local ID・bag/storage 順序・装備・quality・rarity・affix・upgrade・durability は中央保存します。削除 ID 履歴も保存します。戦闘・価格・報酬判定は UE 権威で、local `.sav` 自動 upload はありません。詳細 DTO・error・移行規則は API 契約に従います。

## 이번 별도 인스턴스

## 今回の別 instance

다음 명령은 상태 조회가 아니라 새 PostgreSQL·API 기동과 migration 1→3을 수행합니다. 아래 고정 이름의 예시 명령은 직접 실행하지 않았습니다. 실제 검증에서는 작업별 고유 컨테이너·volume·schema를 생성하고 같은 DB를 유지한 API 재시작과 재접속을 확인했습니다. 데스크톱의 Docker와 Release 빌드 준비 후 사용합니다.

次は state 読み取りでなく、新 PostgreSQL/API の起動と migration 1→3 を行うコマンドです。以下の固定名の例は直接実行していません。実検証では job 固有 container・volume・schema を作り、同じ DB を保つ API 再起動・再接続を確認しました。desktop の Docker と Release build を準備してから使います。

```powershell
pwsh -NoProfile -File .\Tools\Start-Backend.ps1 `
  -ContainerName fablewood-onlineflow-20261023-postgres `
  -VolumeName fablewood-onlineflow-20261023-data `
  -ComposeProject fablewood-onlineflow-20261023 `
  -DatabasePort 55440 -ApiPort 5292 `
  -Schema fable_onlineflow_20261023 -ServerId onlineflow-dedicated-20261023
```

예약 주소는 API `http://127.0.0.1:5292`, PostgreSQL `127.0.0.1:55440`, DB 이름 `fablewood`입니다. 실행기 기본값은 기존 서비스이므로 이번 작업에는 위 격리 인자를 반드시 명시합니다. 원본 `5281/55438`, Inventory `5291/55439`나 기존 볼륨·키를 재사용하지 않습니다.

予約接続先は API `http://127.0.0.1:5292`、PostgreSQL `127.0.0.1:55440`、DB 名 `fablewood`。launcher の既定値は既存 service のため、今回の作業には上記隔離引数を必ず指定します。原本 `5281/55438`、Inventory `5291/55439`、既存 volume/key は再利用しません。

기본 서버 목록은 `onlineflow-local`, 이름 `동화숲`, 주소 `127.0.0.1:7821`입니다. 수동 환경 설정은 `FABLE_GAME_SERVER_ID`, `FABLE_GAME_SERVER_NAME`, `FABLE_GAME_ADDRESS`를 사용하며 Unreal 포트와 일치시킵니다. 현재 IPv4/DNS:port를 지원하고 IPv6 주소는 지원하지 않습니다.

既定 server list は `onlineflow-local`、名前 `동화숲`、接続先 `127.0.0.1:7821` です。手動環境設定は `FABLE_GAME_SERVER_ID`、`FABLE_GAME_SERVER_NAME`、`FABLE_GAME_ADDRESS` を使い、Unreal port と一致させます。現在 IPv4/DNS:port に対応し、IPv6 は未対応です。

비밀번호·서버 키·UE 설정·소유 상태는 이 루트의 `Saved/Backend/`에 생성합니다. 대응 자격 증명이 없는 기존 볼륨, 다른 경로·포트·schema·소유 프로세스는 초기화하지 않고 거절합니다. `-Restart`는 실행 파일·PID·시작 시각이 확인된 이 작업본의 API에만 적용하고 DB 볼륨을 유지합니다. 설정을 다른 작업본에서 복사하지 않습니다.

password・server key・UE 設定・所有 state はこの root の `Saved/Backend/` に生成します。対応 credential のない既存 volume、別 path・port・schema・所有 process は初期化せず拒否します。`-Restart` は実行ファイル・PID・開始時刻を確認したこのコピーの API だけに適用し、DB volume は維持します。別コピーから設定をコピーしません。

NTFS에서 비공개 폴더 ACL을 제한하며 exFAT에는 사용자별 ACL이 없습니다. DB 연결·키를 소스나 공개 로그·명령줄에 넣지 않습니다. 원격 API는 HTTPS가 필수입니다. `/health`는 DB와 스키마 3을 확인합니다. 입장권은 180초·단일 사용, writer 리스는 60초입니다.

NTFS では非公開 folder ACL を制限し、exFAT には user 別 ACL がありません。DB 接続/key を source、公開 log、command line に入れません。remote API は HTTPS 必須。`/health` は DB と schema 3 を検査します。ticket は 180 秒・単一使用、writer lease は 60 秒です。

## Unreal 연동과 Git 입력

## Unreal 接続と Git 入力

백엔드 준비 후 형제 게임에서 `Start-OnlineServer.cmd -BackendRoot ..\CatPjt-Fablewood-Backend -Port 7821`, `Play-Account.cmd -BackendUrl http://127.0.0.1:5292`를 사용합니다. 서버 실행기는 해당 백엔드의 저장된 소유 상태와 비공개 `server.json`을 선택합니다. 새 계정 화면의 검증은 게임 `Tools/Test-OnlineFlow.ps1`이 담당합니다. 독립 Server Target·Linux·외부망·이번 Windows Game 패키지 실행 플레이는 미검증입니다.

backend 準備後、兄弟 game で `Start-OnlineServer.cmd -BackendRoot ..\CatPjt-Fablewood-Backend -Port 7821`、`Play-Account.cmd -BackendUrl http://127.0.0.1:5292` を使います。server launcher は当該 backend の保存済み所有 state と非公開 `server.json` を選びます。新 account 画面検証は game の `Tools/Test-OnlineFlow.ps1` が担当します。この記録時点の独立 Server Target・Linux・外部 network・今回 Windows Game package の play は未検証です。

```powershell
pwsh -NoProfile -File .\Tools\Get-SourceManifest.ps1
```

매니페스트는 API·필수 통합 검사·SQL 001~003·Tools·SDK pin·lock·허용된 루트 파일을 SHA-256과 함께 수집합니다. `Saved`, `bin/obj`, artifacts, DB 볼륨·사용자 데이터·자격 증명·키·실행 상태·로그는 제외합니다. 형제 게임의 필수 `.uasset/.umap`과 원본 OBJ는 게임 매니페스트가 별도로 포함합니다. 매니페스트는 빌드·실행 결과나 모든 비밀의 부재를 보증하지 않습니다. 테스트 스키마와 증거는 자동 삭제하지 않습니다.

manifest は API・必須統合検査・SQL 001～003・Tools・SDK pin・lock・許可された root file を SHA-256 とともに収集します。`Saved`、`bin/obj`、artifacts、DB volume・user data・credential・key・実行 state・log は除外します。兄弟 game の必須 `.uasset/.umap` と原本 OBJ は game manifest が別に含めます。manifest は build・実行結果や全秘密情報の不在を保証しません。test schema と根拠は自動削除しません。
