# Fablewood 중앙 저장 API / Fablewood 中央保存 API

ASP.NET Core 10 / Npgsql 10.0.3 / PostgreSQL 18. 이 서비스는 계정·접속 권한·원자 저장을 담당합니다. UE 전용 서버가 전투와 거래 규칙을 판정하고, API는 SaveGame 바이트를 불투명한 데이터로 보관하고, 장비 목록과 재화는 별도 정형 테이블에도 함께 저장합니다. 새 UE 저장 형식은 v9이며 기존 v8 이하는 UE에서 검증·이전합니다. 클라이언트는 프로필·재화를 변경할 수 없습니다.

ASP.NET Core 10 / Npgsql 10.0.3 / PostgreSQL 18。このサービスはアカウント・接続権限・原子的な保存を担当します。UE 専用サーバーが戦闘と取引ルールを判定し、API は SaveGame のバイト列を不透明なデータとして保管するとともに、装備一覧と通貨等を別の構造化テーブルにも保存します。新しい UE 保存形式は v9 で、既存の v8 以下は UE で検証・移行します。クライアントはプロフィール・通貨等を変更できません。

## 환경과 실행 / 環境と実行

- `FABLE_DB_CONNECTION`: PostgreSQL 연결 문자열. 비밀 파일/프로세스 환경으로만 전달합니다. 예: `Host=127.0.0.1;Port=55438;Database=fablewood;Username=<private-user>;Password=<private-password>;Maximum Pool Size=20;Timeout=5;Command Timeout=10`.
- `FABLE_DB_SCHEMA`: 기본 `fablewood`. ASCII 소문자·숫자·밑줄의 안전한 식별자만 허용합니다.
- `FABLE_SERVER_KEY_FILE`: 32~512자의 UTF-8 원문 키 파일. 앞뒤 공백은 제거합니다. 또는 `FABLE_SERVER_KEY` 중 하나만 설정합니다. 키를 Git, URL, 명령행 인자에 넣지 않습니다.
- `ASPNETCORE_URLS`: 개발 PC에서는 `http://127.0.0.1:5281`. 원격 접속은 HTTPS가 필수입니다. 프록시 전달 헤더를 자동으로 신뢰하지 않습니다.

- `FABLE_DB_CONNECTION`: PostgreSQL 接続文字列。秘密ファイル/プロセス環境だけで渡します。例: `Host=127.0.0.1;Port=55438;Database=fablewood;Username=<private-user>;Password=<private-password>;Maximum Pool Size=20;Timeout=5;Command Timeout=10`。
- `FABLE_DB_SCHEMA`: 既定値は `fablewood`。ASCII 小文字・数字・アンダースコアによる安全な識別子のみを許可します。
- `FABLE_SERVER_KEY_FILE`: 32～512 文字の UTF-8 平文キーファイル。前後の空白は除去します。これと `FABLE_SERVER_KEY` のいずれか一方だけを設定します。キーを Git、URL、コマンドライン引数に含めません。
- `ASPNETCORE_URLS`: 開発 PC では `http://127.0.0.1:5281`。リモート接続には HTTPS が必須です。プロキシの転送ヘッダーは自動的に信頼しません。

`dotnet restore --locked-mode` 후 `dotnet build -c Release --no-restore`로 빌드합니다. 키가 없거나 약한 길이면 시작을 거절합니다. `Fablewood.Backend.exe --migrate`는 DB 스키마 1 → 2를 순서대로 한 트랜잭션에서 적용한 뒤 종료합니다. 이미 적용된 버전은 건너뛰며 스키마 2 이후의 알 수 없는 DB 버전은 거절합니다. 성공 종료코드는 0이며, 서비스 시작은 별도 `Fablewood.Backend.exe`입니다. 마이그레이션은 자동 실행하지 않습니다. 운영 DB 계정에는 스키마 적용용 DDL 권한과 서비스용 최소 DML 권한을 분리하는 구성이 권장됩니다.

`dotnet restore --locked-mode` の後に `dotnet build -c Release --no-restore` でビルドします。キーがない場合や長さが不十分な場合は起動を拒否します。`Fablewood.Backend.exe --migrate` は DB スキーマ 1 → 2 を順に 1 トランザクションで適用して終了します。適用済みバージョンはスキップし、スキーマ 2 より後の未知の DB バージョンは拒否します。成功終了コードは 0 で、サービスは別途 `Fablewood.Backend.exe` で起動します。移行は自動実行しません。本番 DB アカウントは、スキーマ適用用 DDL 権限とサービス用の最小 DML 権限を分離する構成を推奨します。

## API 계약 / API 契約

JSON은 camelCase이며 정의하지 않은 요청 필드는 400입니다. 계정명은 대소문자를 구분하지 않는 `[A-Za-z0-9_]` 3~32자, 비밀번호는 10~128자입니다. PBKDF2-SHA256 600,000회·개별 32바이트 salt·고정시간 비교를 사용합니다. 로그인과 가입에는 IP별 분당 20회 제한을 공유합니다.

JSON は camelCase で、定義されていないリクエストフィールドは 400 です。アカウント名は大文字・小文字を区別しない `[A-Za-z0-9_]` の 3～32 文字、パスワードは 10～128 文字です。PBKDF2-SHA256 を 600,000 回、個別の 32 バイト salt、定時間比較を使用します。ログインと登録は、IP ごとに毎分 20 回という共通制限を使用します。

| 요청 | 인증 | 요청 본문 / 응답 |
| --- | --- | --- |
| `GET /health` | 없음 | DB 연결·스키마2 확인 후 `{status:"ok"}`, 미준비 503 |
| `POST /v1/accounts` | 없음 | `{name,password,initialClass?:0..2}` → 201 `{playerId}` |
| `POST /v1/sessions` | 없음 | `{name,password}` → `{accessToken,expiresAt}`; 토큰 20분 |
| `POST /v1/join-tickets` | Bearer | `{}` → `{ticket,expiresAt}`; 입장권 180초(3분)·한 번 사용 |
| `GET /v1/me` | Bearer | `{playerId,revision,wallet,profileHash,leaseActive}`; 자기 지갑만, blob 미공개 |
| `POST /v1/server/claim` | 서버 키 | `{ticket,serverId}` → `{playerId,leaseId,leaseExpiry,revision,initialClass,profile,profileHash,wallet,inventoryVersion,inventory}` |
| `POST /v1/server/heartbeat` | 서버 키 | `{playerId,leaseId}` → `{leaseExpiry}`; DB 시각 기준 60초 갱신 |
| `POST /v1/server/commit` | 서버 키 | `{playerId,leaseId,operationId,expectedRevision,profile,wallet}` → `{revision,profileHash}` |
| `GET /v1/me/inventory` | Bearer | `{playerId,revision,profileHash,inventoryVersion,inventory}`; 자기 장비 목록만 |
| `POST /v1/server/commit-inventory` | 서버 키 | 기존 commit 본문 + `inventory` → `{revision,profileHash,inventoryVersion:1}` |
| `POST /v1/server/release` | 서버 키 | `{playerId,leaseId}` → `{released:true}`; 최신 저장 ACK 이후 호출 |

| リクエスト | 認証 | リクエスト本文 / レスポンス |
| --- | --- | --- |
| `GET /health` | なし | DB 接続・スキーマ2 の確認後に `{status:"ok"}`、準備未完了は 503 |
| `POST /v1/accounts` | なし | `{name,password,initialClass?:0..2}` → 201 `{playerId}` |
| `POST /v1/sessions` | なし | `{name,password}` → `{accessToken,expiresAt}`; トークンは 20 分 |
| `POST /v1/join-tickets` | Bearer | `{}` → `{ticket,expiresAt}`; 入場券は 180 秒（3 分）・1 回限り |
| `GET /v1/me` | Bearer | `{playerId,revision,wallet,profileHash,leaseActive}`; 自分の wallet のみ、blob は非公開 |
| `POST /v1/server/claim` | サーバーキー | `{ticket,serverId}` → `{playerId,leaseId,leaseExpiry,revision,initialClass,profile,profileHash,wallet,inventoryVersion,inventory}` |
| `POST /v1/server/heartbeat` | サーバーキー | `{playerId,leaseId}` → `{leaseExpiry}`; DB 時刻を基準に 60 秒更新 |
| `POST /v1/server/commit` | サーバーキー | `{playerId,leaseId,operationId,expectedRevision,profile,wallet}` → `{revision,profileHash}` |
| `GET /v1/me/inventory` | Bearer | `{playerId,revision,profileHash,inventoryVersion,inventory}`; 自分の装備一覧のみ |
| `POST /v1/server/commit-inventory` | サーバーキー | 既存の commit 本文 + `inventory` → `{revision,profileHash,inventoryVersion:1}` |
| `POST /v1/server/release` | サーバーキー | `{playerId,leaseId}` → `{released:true}`; 最新保存の ACK 後に呼び出す |

서버 키 헤더는 `X-Fable-Server-Key`입니다. 서버 키는 UE 전용 서버의 비공개 설정에만 전달합니다. 세션·입장권은 256비트 난수이며 DB에는 SHA256만 저장합니다. `profile`은 공백 없는 정규 Base64, 디코딩 기준 1~262144바이트입니다. `profileHash`는 raw bytes SHA256의 소문자 hex이며 새 프로필은 null입니다. 초기 클래스는 계정 생성 시 저장합니다.

サーバーキーヘッダーは `X-Fable-Server-Key` です。サーバーキーは UE 専用サーバーの非公開設定にのみ渡します。セッション・入場券は 256 ビット乱数で、DB には SHA256 のみを保存します。`profile` は空白を含まない正規 Base64 で、デコード後のサイズは 1～262144 バイトです。`profileHash` は raw bytes の SHA256 を小文字の hex にしたもので、新規プロフィールでは null です。初期クラスはアカウント作成時に保存します。

`wallet`은 `{gold,ore,essence,rootCores,moonCrystals,healthPotions,manaPotions}`입니다. 골드 0~1,000,000, 광석·재료 0~100,000, 물약 0~20입니다. 새 계정은 재화 0, 체력 물약 3개, 마나 물약 2개입니다. API는 거래 가격을 추측하지 않으며, UE 서버가 계산한 스냅샷만 받습니다. 음수 지갑과 범위 초과는 DB 변경 전에 거절합니다.

`wallet` は `{gold,ore,essence,rootCores,moonCrystals,healthPotions,manaPotions}` です。ゴールドは 0～1,000,000、鉱石・素材は 0～100,000、ポーションは 0～20 です。新規アカウントは通貨・素材 0、体力ポーション 3 個、マナポーション 2 個です。API は取引価格を推測せず、UE サーバーが計算したスナップショットだけを受け取ります。負の wallet 値や範囲超過は DB 変更前に拒否します。

## 동시 저장과 장애 복구 / 同時保存と障害復旧

한 플레이어는 유효한 리스 하나만 가집니다. 재접속은 기존 리스가 해제되거나 만료될 때까지 거절합니다. 리스 UUID는 접속마다 새로 발급되며 만료 후 되살릴 수 없습니다. heartbeat를 20초 간격으로 보내고, 실패 시 저장·플레이 진행을 중단하는 것이 클라이언트 서버의 책임입니다.

1 プレイヤーが持てる有効なリースは 1 つだけです。既存リースが解放または失効するまで再接続を拒否します。リース UUID は接続ごとに新規発行し、失効後に復活させることはできません。heartbeat を 20 秒間隔で送信し、失敗時に保存・プレイの進行を止める責任は、API を利用するゲームサーバー側にあります。

commit은 프로필 행을 `SELECT FOR UPDATE`로 잠근 뒤 작업 ID·리스·리비전을 검사합니다. 프로필, 지갑, 작업 기록, 재화 차이 ledger를 같은 트랜잭션에서 확정합니다. 동일 작업 ID·동일 본문의 재전송은 처음 ACK를 반환합니다(이미 만료된 리스의 읽기 전용 replay도 허용). 동일 ID에 다른 본문은 409입니다. 다른 ID로 같은 리비전을 동시에 쓰면 하나만 성공합니다. 충돌 시 임의로 새 리비전을 붙여 덮어쓰지 말고 서버 세션을 재동기화해야 합니다.

commit はプロフィール行を `SELECT FOR UPDATE` でロックした後、操作 ID・リース・リビジョンを検査します。プロフィール、wallet、操作記録、通貨等の差分 ledger を同じトランザクションで確定します。同一操作 ID・同一本文の再送には最初の ACK を返します（失効済みリースの読み取り専用 replay も許可）。同一 ID に異なる本文を送ると 409 です。異なる ID で同じリビジョンへ同時に書き込むと、1 件だけが成功します。衝突時は任意の新しいリビジョンを付けて上書きせず、サーバーセッションを再同期する必要があります。

오류는 `{code,message}`이며 인증 401, 원격 HTTP 403, 입력 범위 400, 리스/리비전/작업 충돌 409, DB 불가 503입니다. lease release 재전송은 이미 종료된 경우 409를 반환하며 다른 새 리스를 해제하지 않습니다. DB 오류 응답/로그에는 SQL 인자·비밀·프로필 바이트를 출력하지 않습니다. 유효기간이 지난 세션·입장권과 오래된 ledger 정리는 이 시제품에서 자동 삭제하지 않습니다.

エラー形式は `{code,message}` で、認証は 401、リモート HTTP は 403、入力範囲は 400、リース/リビジョン/操作の衝突は 409、DB 利用不可は 503 です。lease release の再送は、すでに終了している場合に 409 を返し、別の新しいリースを解放しません。DB エラーのレスポンス/ログには SQL 引数・秘密値・プロフィールのバイト列を出力しません。この試作品では、期限切れセッション・入場券や古い ledger を自動削除しません。

## 장비 중앙 저장과 기존 프로필 이전 / 装備の中央保存と既存プロフィールの移行

DB 스키마 버전 2와 `inventoryVersion:1`은 UE SaveGame 버전 9와 구별합니다. 아직 장비를 이전하지 않은 프로필은 `inventoryVersion:0, inventory:null`입니다. 새 서버는 claim의 blob을 UE에서 검증하고 기존 장비 ID·성능·옵션·품질·등급·강화·내구도·배열 순서를 유지하며 누락된 장비 UUID를 한 번 부여합니다. 최초 `commit-inventory` ACK 이후에 플레이를 허용합니다. 기존 blob을 API가 직접 디코드하거나 추측하여 이전하지 않습니다.

DB スキーマバージョン 2 と `inventoryVersion:1` は、UE SaveGame バージョン 9 と区別します。装備が未移行のプロフィールは `inventoryVersion:0, inventory:null` です。新サーバーは claim の blob を UE で検証し、既存の装備 ID・性能・オプション・品質・レアリティ・強化・耐久度・配列順序を維持しながら、欠けている装備 UUID を 1 回付与します。最初の `commit-inventory` ACK 後にプレイを許可します。API が既存 blob を直接デコードしたり推測したりして移行することはありません。

`inventory`의 필드는 `{version:1,heroClass,nextLocalId,equippedWeaponInstanceId,equippedArmorInstanceId,items}`입니다. `items`는 가방(`location:0`)을 위치순으로 쓴 뒤 창고(`location:1`)를 위치순으로 씁니다. 각 위치는 0부터 연속이며 가방은 4~32개, 창고는 0~48개입니다. 장비 항목은 아래 필드를 모두 보냅니다.

`inventory` のフィールドは `{version:1,heroClass,nextLocalId,equippedWeaponInstanceId,equippedArmorInstanceId,items}` です。`items` はバッグ（`location:0`）を位置順に記述した後、倉庫（`location:1`）を位置順に記述します。各位置は 0 から連続し、バッグは 4～32 個、倉庫は 0～48 個です。装備項目では以下の全フィールドを送信します。

```text
instanceId, localId, location, position, slot, heroClass, rarity, power,
upgrade, rollVersion, quality, nominalPower, rollPercent, setId,
durabilityVersion, durability, maxDurability, affixes:[{stat,value}]
```

UUID는 빈 값이 아니며 전체 DB에서 한 소유자에게만 속합니다. 기존 로컬 ID도 같은 소유자 안에서 재사용할 수 없습니다. 서버가 목록에서 제거한 장비는 `retired` 상태로 남고, UUID 또는 로컬 ID를 바꿔 되살릴 수 없습니다. 새 로컬 ID는 직전 `nextLocalId` 이상이어야 하며 다음 ID는 감소할 수 없습니다. 단 최초 v0 백필은 시작 장비 ID 1~4를 포함한 기존 ID를 그대로 받아들입니다. 시작 장비 4개는 항상 가방에 있고 장착 UUID는 자기 가방의 해당 슬롯이어야 합니다. 무기는 현재 직업과 일치해야 합니다.

UUID は空ではなく、DB 全体で 1 所有者だけに属します。既存のローカル ID も同一所有者内では再利用できません。サーバーが一覧から除いた装備は `retired` 状態で残り、UUID またはローカル ID を変えて復活させることはできません。新しいローカル ID は直前の `nextLocalId` 以上で、次の ID は減少できません。ただし初回の v0 バックフィルでは、初期装備 ID 1～4 を含む既存 ID をそのまま受け入れます。初期装備 4 個は常にバッグ内にあり、装着 UUID は自分のバッグの該当スロットでなければなりません。武器は現在のクラスと一致する必要があります。

API는 슬롯/직업/등급 범위, 원래 고정 장비와 난수 장비의 형식, 품질별 굴림 범위와 정수 반올림 결과, 최대 3개의 중복 없는 옵션, 옵션별 수치, 전설 장비 상한 104 및 나머지 장비 상한 100, 내구도 버전과 등급별 최대치를 검증합니다. 마나 회복 옵션 값은 초당 수치의 10배인 정수입니다. 옵션 배열의 순서는 그대로 저장하며 재정렬하지 않습니다.

API はスロット/クラス/レアリティの範囲、元の固定装備とランダム装備の形式、品質別のロール範囲と整数丸め結果、重複しない最大 3 個のオプション、各オプション値、伝説装備の上限 104 とその他の装備の上限 100、耐久度バージョンとレアリティ別最大値を検証します。マナ回復オプション値は、毎秒の値の 10 倍を表す整数です。オプション配列の順序はそのまま保存し、並べ替えません。

`commit-inventory`는 프로필 행 잠금 아래에서 리스·CAS·작업 해시를 확인하고 blob, 재화, 장착 참조, 장비 및 옵션 행, 재화 ledger, 장비 변경 journal을 한 트랜잭션으로 확정합니다. journal은 이전/이후 전체 정형 목록을 보관하고 동일 작업 재전송은 새 행을 만들지 않습니다. 위치의 지연 UNIQUE 제약은 장비 위치 교환을 원자적으로 처리합니다. 서로 다른 소유자가 같은 새 UUID를 동시에 사용하면 전역 기본 키가 한 쪽을 거절하고 해당 거래 전체를 되돌립니다.

`commit-inventory` はプロフィール行ロックの下でリース・CAS・操作ハッシュを確認し、blob、通貨等、装着参照、装備とオプション行、通貨等の ledger、装備変更 journal を 1 トランザクションで確定します。journal は変更前/変更後の構造化一覧全体を保存し、同一操作の再送では新しい行を作りません。位置に対する遅延評価の UNIQUE 制約によって、装備の位置交換を原子的に処理します。異なる所有者が同じ新規 UUID を同時に使用すると、グローバルな主キー制約が片方を拒否し、その取引全体を取り消します。

새 작업 해시는 요청 DTO 전체와 전용 구분자를 포함합니다. JSON 객체의 속성 순서는 역직렬화 후 정규화되지만 장비·옵션 배열 순서는 해시에 포함됩니다. 응답을 잃으면 같은 작업 ID·같은 직렬화 본문을 재전송해야 합니다. 장비가 이전된 뒤에는 기존 `/commit`의 새로운 변경을 409 `inventory_required`로 거절합니다. 이전에 성공한 기존 `/commit`의 동일 요청 replay는 원래 receipt를 유지합니다.

新しい操作ハッシュにはリクエスト DTO 全体と専用の区切り文字を含めます。JSON オブジェクトのプロパティ順序は逆シリアル化後に正規化しますが、装備・オプション配列の順序はハッシュに含みます。応答を失った場合は、同じ操作 ID・同じシリアル化本文を再送する必要があります。装備移行後、既存 `/commit` の新しい変更は 409 `inventory_required` で拒否します。以前に成功した既存 `/commit` の同一リクエスト replay は、元の receipt を維持します。

API는 blob과 정형 목록의 의미적 동일성을 직접 확인할 수 없습니다. UE 전용 서버가 동일한 검증 완료 SaveGame에서 두 표현을 만들고, 다음 claim에서는 원본 v9 blob과 정형 목록을 모든 필드·순서·장착 참조까지 대조해야 합니다. 불일치를 덮어쓰기나 병합으로 해결하지 않고 접속을 중단하는 것이 계약입니다. 실제 거래 가격, 아이템 생성 조건 및 보상은 UE 권위이며 API는 이를 재계산하지 않습니다.

API は blob と構造化一覧の意味的な同一性を直接確認できません。UE 専用サーバーが同じ検証済み SaveGame から両方の表現を作成し、次回 claim では原本 v9 blob と構造化一覧を全フィールド・順序・装着参照まで照合する必要があります。不一致を上書きやマージで解消せず、接続を中止する契約です。実際の取引価格、アイテム生成条件、報酬の判定権限は UE にあり、API は再計算しません。

## 집중 통합 테스트 / 重点統合テスト

형제 프로젝트 `Fablewood.Backend.IntegrationTests`는 외부 테스트 패키지 없이 실제 loopback HTTP + 고유 `fable_test_<uuid>` PostgreSQL 스키마를 사용합니다. 실행자가 DB를 준비한 뒤 `dotnet run --project ../Fablewood.Backend.IntegrationTests -c Release --no-build -- --output <새로운 중앙 증거 폴더>`를 실행합니다. 기존 출력 폴더는 거절하며 기존 게임 저장/제품 schema는 변경하지 않습니다.

兄弟プロジェクト `Fablewood.Backend.IntegrationTests` は外部テストパッケージを使用せず、実際の loopback HTTP と固有の `fable_test_<uuid>` PostgreSQL スキーマを使用します。実行者が DB を準備した後、`dotnet run --project ../Fablewood.Backend.IntegrationTests -c Release --no-build -- --output <새로운 중앙 증거 폴더>` を実行します（末尾のプレースホルダーは新しい中央証拠フォルダーです）。既存の出力フォルダーは拒否し、既存のゲーム保存/製品 schema は変更しません。

기존 검사 대상: 클라이언트/잘못된 서버 키 거절, 계정명 중복, 비밀번호 인증, 계정의 미정의 gold 필드 거절, 초기 지갑, 발급 응답과 DB 만료 시각 일치·180초(3분) 유효기간·단일사용/만료 입장권, 중복 접속, 음수/초과 지갑, 비정규/초과 profile, 동일 작업 재시도/다른 본문 충돌, 동시 revision 승자 한 명, ledger 실패시 전부 rollback, 리스 갱신/만료/이전 writer 차단, 타 계정 지갑 격리. 시간 만료·ledger 오류 주입은 오직 새 테스트 schema에서 수행합니다.

既存の検査対象: クライアント/不正サーバーキーの拒否、アカウント名の重複、パスワード認証、アカウントの未定義 gold フィールドの拒否、初期 wallet、発行レスポンスと DB 有効期限の一致・180 秒（3 分）の有効期間・単一使用/期限切れ入場券、重複接続、負数/上限超過の wallet、非正規/上限超過の profile、同一操作の再試行/異なる本文の衝突、同時 revision の成功者が 1 件のみであること、ledger 失敗時の全 rollback、リースの更新/失効/以前の writer の遮断、別アカウントの wallet 分離。期限切れ・ledger 障害の注入は、新しいテスト schema 内だけで行います。

장비 확장 검사는 별도 계정에서 v0 백필, 정형 DB 행과 API 결과 일치, 타 계정 소유권, UUID/로컬 ID 중복·폐기 후 재사용 거절, 가방/창고 이동, 위치 교환, 옵션 순서, 장착 조건, 범위 오류, 동일 요청 재시도, 동시 CAS, journal 오류 시 전체 rollback을 검사하도록 구성합니다. 이 문서는 구현 계약이며 테스트 실행 성공은 별도 실행 보고서로 확인해야 합니다.

装備拡張検査は別アカウントで、v0 バックフィル、構造化 DB 行と API 結果の一致、別アカウントの所有権、UUID/ローカル ID の重複・廃棄後の再利用拒否、バッグ/倉庫間の移動、位置交換、オプション順序、装着条件、範囲エラー、同一リクエストの再試行、同時 CAS、journal エラー時の全 rollback を検査する構成です。この文書は実装契約であり、テスト実行の成功は別の実行報告書で確認する必要があります。

테스트는 `persistence-checkpoint.json`을 쓰고 schema를 보존합니다. 운영자가 해당 테스트 DB의 PostgreSQL 재시작을 수행한 다음 `--verify-persistence <checkpoint.json>`으로 새 연결에서 raw profile SHA·wallet·revision·operation/ledger 수를 비교합니다. 이 검사는 PostgreSQL을 직접 재시작하지 않으며 재시작 사실은 운영자가 별도 증거로 남겨야 합니다. 완료한 테스트 스키마/증거도 자동 삭제하지 않습니다.

テストは `persistence-checkpoint.json` を書き出し、schema を保全します。運用者が該当テスト DB の PostgreSQL を再起動した後、`--verify-persistence <checkpoint.json>` を使って新しい接続から raw profile SHA・wallet・revision・operation/ledger 件数を比較します。この検査は PostgreSQL を直接再起動せず、再起動の事実は運用者が別途証拠に残す必要があります。完了したテストスキーマ/証拠も自動削除しません。

구현 참고: [Npgsql 기본 사용법](https://www.npgsql.org/doc/basic-usage.html), [ASP.NET Core 10 rate limiting](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0).

実装参考: [Npgsql の基本的な使い方](https://www.npgsql.org/doc/basic-usage.html)、[ASP.NET Core 10 rate limiting](https://learn.microsoft.com/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)。
