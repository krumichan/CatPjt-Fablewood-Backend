using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fablewood.Backend;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

/// <summary>백엔드 또는 격리 통합 검증의 실행 진입점.</summary>
internal static partial class Program
{
    private const string InventoryCommitRoute = "/v1/server/commit-inventory";

    /// <summary>플레이어별 인벤토리와 재화의 재시작 전 기준 상태.</summary>
    private sealed record InventoryPlayerCheckpoint(Guid PlayerId, long Revision, string ProfileHash, Wallet Wallet,
                                                    InventorySnapshot Inventory, long Operations, long LedgerRows,
                                                    long JournalRows, string DatabaseFingerprint);

    /// <summary>인벤토리 영구 저장 검증의 재시작 전 기준 상태.</summary>
    private sealed record InventoryCheckpoint(int FormatVersion, string Schema, InventoryPlayerCheckpoint[] Players);

    /// <summary>인벤토리 통합 검증에 사용하는 격리 계정·접속 권한·기대 상태.</summary>
    private sealed record InventoryActor(Guid PlayerId, Guid LeaseId, HttpClient Client);

    /// <summary>영구 저장 계약 검증에 사용할 불투명 프로필 바이트를 만든다.</summary>
    private static string Blob(string label)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes("opaque-inventory-contract-fixture:" + label));
    }

    /// <summary>불투명 프로필 바이트의 저장 비교용 해시를 계산한다.</summary>
    private static string BlobHash(string blob)
    {
        return Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(blob)));
    }

    /// <summary>JSON의 속성 순서와 무관하게 같은 저장 값인지 비교한다.</summary>
    private static bool SameJson<T>(T left, T right)
    {
        return JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, Json),
                                      JsonSerializer.SerializeToElement(right, Json));
    }

    /// <summary>지정한 항목만 바꾼 인벤토리 검증 입력을 만든다.</summary>
    private static InventoryItem Change(InventoryItem item, int location, int position)
    {
        return item with { Location = location, Position = position };
    }

    /// <summary>인벤토리 검증 입력에서 지정한 아이템을 교체한다.</summary>
    private static InventorySnapshot ReplaceItem(InventorySnapshot snapshot, int localId,
                                                 Func<InventoryItem, InventoryItem> replace)
    {
        return snapshot with {
            Items = snapshot.Items.Select(item => item.LocalId == localId ? replace(item) : item).ToArray()
        };
    }

    /// <summary>인벤토리 검증 입력에 필요한 값을 구성하거나 해당 검증 단계를 처리한다.</summary>
    private static InventorySnapshot InventoryFixture(int heroClass)
    {
        /// <summary>시작 장비에 필요한 값을 구성하거나 해당 검증 단계를 처리한다.</summary>
        InventoryItem Starter(int id, int slot, int itemClass, int power)
        {
            return new(Guid.NewGuid(), id, 0, id - 1, slot, itemClass, 0, power, 0, 0, 0, 0, 100, 0, 1, 60, 60, []);
        }
        var items = new[] { Starter(1, 0, 0, 24),
                            Starter(2, 0, 1, 24),
                            Starter(3, 0, 2, 24),
                            Starter(4, 1, 0, 0),
                            new InventoryItem(Guid.NewGuid(), 5, 0, 4, 0, heroClass, 3, 70, 0, 1, 3, 58, 120, 2, 1, 120,
                                              120, [new ItemAffix(3, 15), new ItemAffix(0, 6)]),
                            new InventoryItem(Guid.NewGuid(), 6, 1, 0, 1, 0, 2, 12, 0, 1, 2, 12, 100, 1, 0, 100, 100,
                                              [new ItemAffix(1, 4), new ItemAffix(2, 20)]) };

        return new(1, heroClass, 7, items[heroClass].InstanceId, items[3].InstanceId, items);
    }

    /// <summary>구매한 무기에 필요한 값을 구성하거나 해당 검증 단계를 처리한다.</summary>
    private static InventoryItem PurchasedWeapon(int localId, int position, int heroClass)
    {
        return new(Guid.NewGuid(), localId, 0, position, 0, heroClass, 1, 30, 0, 1, 2, 30, 100, 0, 1, 80, 80,
                   [new ItemAffix(0, 2), new ItemAffix(3, 5)]);
    }

    // Read physical columns independently of the API's inventory loader; keep array/affix order observable.

    /// <summary>정렬된 SQL 조회로 인벤토리 계약의 정규 표현을 읽는다.</summary>
    private static async Task<InventorySnapshot> ReadSqlInventory(BackendOptions options, Guid player)
    {
        // SQL 명령과 조회 결과에 필요한 값 준비
        // 원시 SQL 문자열의 본문과 공백을 그대로 보존한다.
        // clang-format off
        const string sql = """
            SELECT jsonb_build_object('version',p.inventory_version,'heroClass',p.hero_class,'nextLocalId',p.next_local_id,
              'equippedWeaponInstanceId',p.equipped_weapon_instance_id,'equippedArmorInstanceId',p.equipped_armor_instance_id,
              'items',COALESCE((SELECT jsonb_agg(jsonb_build_object(
                'instanceId',i.instance_id,'localId',i.local_id,'location',i.location,'position',i.position,
                'slot',i.slot,'heroClass',i.hero_class,'rarity',i.rarity,'power',i.power,'upgrade',i.upgrade,
                'rollVersion',i.roll_version,'quality',i.quality,'nominalPower',i.nominal_power,'rollPercent',i.roll_percent,
                'setId',i.set_id,'durabilityVersion',i.durability_version,'durability',i.durability,'maxDurability',i.max_durability,
                'affixes',COALESCE((SELECT jsonb_agg(jsonb_build_object('stat',a.stat,'value',a.value) ORDER BY a.affix_index)
                  FROM inventory_affixes a WHERE a.instance_id=i.instance_id),'[]'::jsonb)) ORDER BY i.location,i.position)
                FROM inventory_items i WHERE i.owner_player_id=p.player_id AND NOT i.retired),'[]'::jsonb))::text
            FROM profiles p WHERE p.player_id=$1
            """;
        // clang-format on
        var text = (string?)await Sql(options, sql, player) ??
                   throw new InvalidOperationException("Canonical inventory row missing.");

        return JsonSerializer.Deserialize<InventorySnapshot>(text, Json) ??
               throw new InvalidOperationException("Canonical inventory could not be read.");
    }

    /// <summary>상태 불변 검증에 사용할 SQL 프로필·장비·이력 지문을 만든다.</summary>
    private static async Task<string> DatabaseFingerprint(BackendOptions options, Guid player)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        // 원시 SQL 문자열의 본문과 공백을 그대로 보존한다.
        // clang-format off
        const string sql = """
            SELECT jsonb_build_object(
              'profile',(SELECT to_jsonb(p) FROM profiles p WHERE p.player_id=$1),
              'items',(SELECT COALESCE(jsonb_agg(to_jsonb(i) ORDER BY i.instance_id),'[]'::jsonb) FROM inventory_items i WHERE i.owner_player_id=$1),
              'affixes',(SELECT COALESCE(jsonb_agg(to_jsonb(a) ORDER BY a.instance_id,a.affix_index),'[]'::jsonb) FROM inventory_affixes a
                JOIN inventory_items i USING(instance_id) WHERE i.owner_player_id=$1),
              'operations',(SELECT COALESCE(jsonb_agg(to_jsonb(o) ORDER BY o.operation_id),'[]'::jsonb) FROM operations o WHERE o.player_id=$1),
              'ledger',(SELECT COALESCE(jsonb_agg(to_jsonb(l) ORDER BY l.operation_id),'[]'::jsonb) FROM economy_ledger l WHERE l.player_id=$1),
              'journal',(SELECT COALESCE(jsonb_agg(to_jsonb(j) ORDER BY j.operation_id),'[]'::jsonb) FROM inventory_journal j WHERE j.player_id=$1))::text
            """;
        // clang-format on
        var canonical = (string?)await Sql(options, sql, player) ??
                        throw new InvalidOperationException("Database fingerprint missing.");

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>프로필 바이트 재화·물약 잔액 결과가 기대한 저장 상태와 같은지 확인한다.</summary>
    private static async Task AssertBlobWallet(BackendOptions options, Guid player, long revision, string profileHash,
                                               Wallet wallet, string label)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        await using var connection = new NpgsqlConnection(options.ConnectionString);
        await connection.OpenAsync();
        await using var query = new NpgsqlCommand(
            "SELECT revision,profile,gold,ore,essence,root_cores,moon_crystals,health_potions,mana_potions FROM profiles WHERE player_id=$1",
            connection);
        query.Parameters.AddWithValue(player);
        await using var reader = await query.ExecuteReaderAsync();

        // SQL 명령과 조회 결과 조건 검사
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Expected profile missing.");
        }

        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var actualWallet = new Wallet(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                                      reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8));
        Check(reader.GetInt64(0) == revision &&
                  Convert.ToHexStringLower(SHA256.HashData(reader.GetFieldValue<byte[]>(1))) == profileHash &&
                  actualWallet == wallet,
              label + ": canonical SQL revision/blob/wallet exact");
    }

    /// <summary>공개 API와 SQL의 인벤토리·재화가 기대값과 같은지 확인한다.</summary>
    private static async Task AssertInventoryAccount(BackendOptions options, HttpClient user, Guid player,
                                                     long revision, string profileHash, Wallet wallet,
                                                     InventorySnapshot inventory, string label)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var response = await Call(user, "/v1/me/inventory", null, HttpStatusCode.OK);
        Check(response.GetProperty("playerId").GetGuid() == player &&
                  response.GetProperty("revision").GetInt64() == revision &&
                  response.GetProperty("profileHash").GetString() == profileHash &&
                  response.GetProperty("inventoryVersion").GetInt32() == 1 &&
                  SameJson(response.GetProperty("inventory").Deserialize<InventorySnapshot>(Json), inventory),
              label + ": own inventory API exact");
        Check(SameJson(await ReadSqlInventory(options, player), inventory),
              label + ": independent SQL item/affix/position/equipment snapshot exact");
        await AssertBlobWallet(options, player, revision, profileHash, wallet, label);
    }

    /// <summary>인벤토리 요청의 성공 응답과 저장 결과를 확인한다.</summary>
    private static async Task AcceptInventory(BackendOptions options, HttpClient server, HttpClient user,
                                              CommitInventoryRequest request, string label)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var ack = await Call(server, InventoryCommitRoute, request, HttpStatusCode.OK);
        Check(ack.GetProperty("revision").GetInt64() == request.ExpectedRevision + 1 &&
                  ack.GetProperty("profileHash").GetString() == BlobHash(request.Profile) &&
                  ack.GetProperty("inventoryVersion").GetInt32() == 1,
              label + ": ACK exact");
        await AssertInventoryAccount(options, user, request.PlayerId, request.ExpectedRevision + 1,
                                     BlobHash(request.Profile), request.Wallet, request.Inventory, label);
    }

    /// <summary>거부된 요청이 프로필·재화·장비·저장 이력을 바꾸지 않았는지 확인한다.</summary>
    private static async Task RejectUnchanged(BackendOptions options, HttpClient client, object request,
                                              HttpStatusCode expected, string label, Guid[] owners,
                                              string route = InventoryCommitRoute)
    {
        var before = new List<string>();

        // 같은 규칙을 적용할 대상을 순회한다.
        foreach (var owner in owners)
        {
            before.Add(await DatabaseFingerprint(options, owner));
        }

        await Call(client, route, request, expected);
        var after = new List<string>();

        // 같은 규칙을 적용할 대상을 순회한다.
        foreach (var owner in owners)
        {
            after.Add(await DatabaseFingerprint(options, owner));
        }

        Check(before.SequenceEqual(after),
              label + ": all owner profile/items/affixes/operations/ledger/journal unchanged");
    }

    /// <summary>생성 인벤토리 검증 계정에 필요한 값을 구성하거나 해당 검증 단계를 처리한다.</summary>
    private static async Task<InventoryActor> CreateInventoryActor(HttpClient anonymous, HttpClient server,
                                                                   string address, string name, int heroClass)
    {
        // 아이템·장비 옵션·인벤토리와 창고에 필요한 값 준비
        const string password = "Inventory-Integration-Only-123";
        var account = await Call(anonymous, "/v1/accounts", new { name, password, initialClass = heroClass },
                                 HttpStatusCode.Created);
        var session = await Call(anonymous, "/v1/sessions", new { name, password }, HttpStatusCode.OK);
        var user = Client(address, token: session.GetProperty("accessToken").GetString());

        // 접속 권한·저장 리비전·멱등 키 처리
        try
        {
            var ticket =
                (await Call(user, "/v1/join-tickets", new {}, HttpStatusCode.OK)).GetProperty("ticket").GetString();
            var claim = await Call(server, "/v1/server/claim", new { ticket, serverId = "inventory-integration" },
                                   HttpStatusCode.OK);
            Check(claim.GetProperty("inventoryVersion").GetInt32() == 0 &&
                      claim.GetProperty("inventory").ValueKind == JsonValueKind.Null,
                  name + ": new account has explicit unbackfilled inventory v0");
            return new(account.GetProperty("playerId").GetGuid(), claim.GetProperty("leaseId").GetGuid(), user);
        }
        catch
        {
            user.Dispose();
            throw;
        }
    }

    /// <summary>변경 이력 결과가 기대한 저장 상태와 같은지 확인한다.</summary>
    private static async Task AssertJournal(BackendOptions options, Guid player, Guid operation,
                                            InventorySnapshot? before, InventorySnapshot after, string label)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var text =
            (string?)await Sql(
                options,
                "SELECT jsonb_build_object('before',before_snapshot,'after',after_snapshot)::text FROM inventory_journal WHERE player_id=$1 AND operation_id=$2",
                player, operation) ??
            throw new InvalidOperationException("Inventory journal missing.");
        using var document = JsonDocument.Parse(text);
        var previous = document.RootElement.GetProperty("before");
        Check((before is null ? previous.ValueKind == JsonValueKind.Null
                              : SameJson(previous.Deserialize<InventorySnapshot>(Json), before)) &&
                  SameJson(document.RootElement.GetProperty("after").Deserialize<InventorySnapshot>(Json), after),
              label + ": journal before/after snapshots exact");
    }

    /// <summary>인벤토리 확인 지점 기준 상태를 기록한다.</summary>
    private static async Task<InventoryPlayerCheckpoint> CaptureInventoryCheckpoint(BackendOptions options, Guid player,
                                                                                    long revision, string profile,
                                                                                    Wallet wallet,
                                                                                    InventorySnapshot inventory)
    {
        // SQL 명령과 조회 결과에 필요한 값 준비
        var operations =
            Convert.ToInt64(await Sql(options, "SELECT count(*) FROM operations WHERE player_id=$1", player));
        var ledger =
            Convert.ToInt64(await Sql(options, "SELECT count(*) FROM economy_ledger WHERE player_id=$1", player));
        var journal =
            Convert.ToInt64(await Sql(options, "SELECT count(*) FROM inventory_journal WHERE player_id=$1", player));

        return new(player, revision, BlobHash(profile), wallet, inventory, operations, ledger, journal,
                   await DatabaseFingerprint(options, player));
    }

    /// <summary>재시작 전후의 SQL 인벤토리와 체크포인트가 같은지 확인한다.</summary>
    private static async Task VerifyInventoryPersistence(BackendOptions options, InventoryCheckpoint checkpoint)
    {
        Check(checkpoint.FormatVersion == 1 && checkpoint.Schema == options.Schema && checkpoint.Players.Length == 2,
              "inventory checkpoint format/schema/two owners valid");

        // 접속 권한·저장 리비전·멱등 키 순회
        foreach (var expected in checkpoint.Players)
        {
            await Verify(options, new(checkpoint.Schema, expected.PlayerId, expected.Revision, expected.ProfileHash,
                                      expected.Wallet, expected.Operations, expected.LedgerRows));
            Check(SameJson(await ReadSqlInventory(options, expected.PlayerId), expected.Inventory),
                  "persistent canonical item/affix/equipment snapshot exact");
            Check(await DatabaseFingerprint(options, expected.PlayerId) == expected.DatabaseFingerprint,
                  "persistent owner rows including tombstones and journal exact");
            Check(Convert.ToInt64(await Sql(options, "SELECT count(*) FROM inventory_journal WHERE player_id=$1",
                                            expected.PlayerId)) == expected.JournalRows,
                  "persistent inventory journal count exact");
        }

        // 아이템·장비 옵션·인벤토리와 창고 처리
        Check(!checkpoint.Players[0]
                   .Inventory.Items.Select(i => i.InstanceId)
                   .Intersect(checkpoint.Players[1].Inventory.Items.Select(i => i.InstanceId))
                   .Any(),
              "persistent two-owner UUID sets remain disjoint");
    }

    /// <summary>이전 스키마의 프로필이 인벤토리 스키마로 보존 이관되는지 검증한다.</summary>
    private static async Task<object> VerifySchemaOneMigration(BackendOptions configured)
    {
        // SQL 명령과 조회 결과에 필요한 값 준비
        var legacy = Isolated(configured, "fable_test_" + Guid.NewGuid().ToString("N"));
        await Sql(legacy, $"CREATE SCHEMA \"{legacy.Schema}\"");
        var assembly = typeof(SqlStore).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(
            name => name.EndsWith(".Migrations.001_initial.sql", StringComparison.Ordinal));
        using (var stream = assembly.GetManifestResourceStream(resource) ??
                            throw new InvalidOperationException("Original schema-one migration resource missing."))
        {
            using (var reader = new StreamReader(stream))
            {
                await Sql(legacy, await reader.ReadToEndAsync());
            }
        }
        var player = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var bytes = Encoding.UTF8.GetBytes("opaque-original-schema-one-contract-fixture");
        // PostgreSQL positional parameters bind one statement. Keep the fixture atomic without
        // sending four parameterized INSERT statements in a single extended-query command.
        await using (var connection = new NpgsqlConnection(legacy.ConnectionString))
        {
            // 데이터베이스 트랜잭션 처리
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            /// <summary>이관 검증에 사용할 이전 버전의 SQL 행을 추가한다.</summary>
            async Task Insert(string sql, params object[] values)
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                foreach (var value in values)
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = value });
                }
                await command.ExecuteNonQueryAsync();
            }
            await Insert(
                "INSERT INTO accounts(player_id,name,password_salt,password_hash,password_iterations,initial_class) VALUES($1,'migration_fixture',$2,$3,600000,1)",
                player, RandomNumberGenerator.GetBytes(32), RandomNumberGenerator.GetBytes(32));
            await Insert(
                "INSERT INTO profiles(player_id,revision,profile,gold,ore,essence,root_cores,moon_crystals) VALUES($1,1,$2,77,9,8,3,2)",
                player, bytes);
            await Insert("INSERT INTO operations(player_id,operation_id,request_hash,revision) VALUES($1,$2,$3,1)",
                         player, operation, RandomNumberGenerator.GetBytes(32));
            await Insert(
                "INSERT INTO economy_ledger(player_id,operation_id,revision,gold_delta,ore_delta,essence_delta,root_cores_delta,moon_crystals_delta,health_potions_delta,mana_potions_delta) VALUES($1,$2,1,77,9,8,3,2,0,0)",
                player, operation);
            await transaction.CommitAsync();
        }
        // 원시 SQL 문자열의 본문과 공백을 그대로 보존한다.
        // clang-format off
        const string originalRows = """
            SELECT jsonb_build_object('account',(SELECT to_jsonb(a) FROM accounts a WHERE a.player_id=$1),
              'profile',(SELECT to_jsonb(p)-ARRAY['inventory_version','hero_class','next_local_id','equipped_weapon_instance_id','equipped_armor_instance_id'] FROM profiles p WHERE p.player_id=$1),
              'operations',(SELECT jsonb_agg(to_jsonb(o) ORDER BY o.operation_id) FROM operations o WHERE o.player_id=$1),
              'ledger',(SELECT jsonb_agg(to_jsonb(l) ORDER BY l.operation_id) FROM economy_ledger l WHERE l.player_id=$1))::text
            """;
        // clang-format on
        var before = (string?)await Sql(legacy, originalRows, player) ??
                     throw new InvalidOperationException("Schema-one fixture not created.");
        Check(Convert.ToInt32(await Sql(legacy, "SELECT max(version) FROM schema_migrations")) == 1,
              "legacy migration fixture starts at real schema one");
        await using (var store = new SqlStore(legacy))
        {
            await store.Migrate(CancellationToken.None);
            await store.Migrate(CancellationToken.None);
        }
        Check(
            Convert.ToInt32(await Sql(legacy, "SELECT max(version) FROM schema_migrations")) == 3 &&
                (string?)await Sql(legacy, originalRows, player) == before,
            "schema one to three repeated migration preserves every original account/profile/wallet/operation/ledger column");
        Check(Convert.ToInt32(await Sql(legacy, "SELECT inventory_version FROM profiles WHERE player_id=$1", player)) ==
                      0 &&
                  Convert.ToInt64(await Sql(legacy, "SELECT count(*) FROM inventory_items")) == 0 &&
                  Convert.ToInt64(await Sql(legacy, "SELECT count(*) FROM inventory_journal")) == 0,
              "schema migration keeps legacy profile unbackfilled without fabricating items");

        return new { schema = legacy.Schema,
                     playerId = player,
                     originalProfileSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                     originalRowsSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(before))),
                     sourceSchemaVersion = 1,
                     targetSchemaVersion = 3,
                     fixtureScope = "Synthetic backend contract rows in an isolated schema, not a real UE SaveGame." };
    }

    /// <summary>격리 계정으로 인벤토리 계약·충돌·영구 저장 회귀 검증을 실행한다.</summary>
    private static async Task RunInventorySuite(BackendOptions options, string output)
    {
        // SQL 명령과 조회 결과에 필요한 값 준비
        var firstCheck = Checks.Count;
        var legacySchemaMigration = await VerifySchemaOneMigration(options);
        Check(Convert.ToInt32(await Sql(options, "SELECT max(version) FROM schema_migrations")) == 3,
              "inventory migration schema version three after repeated migration");

        var app = BackendApplication.Build(options);
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        InventoryCheckpoint? checkpoint = null;

        // 재화와 작업 비용 처리
        try
        {
            var address =
                app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var anonymous = Client(address);
            using var server = Client(address, options.ServerKey);
            using var wrongKey = Client(address, "not-an-inventory-server-key");
            var hero = await CreateInventoryActor(anonymous, server, address, "inventory_hero", 1);
            var peer = await CreateInventoryActor(anonymous, server, address, "inventory_peer", 2);
            using var user = hero.Client;
            using var other = peer.Client;
            var owners = new[] { hero.PlayerId, peer.PlayerId };
            var inventory = InventoryFixture(1);
            var peerInventory = InventoryFixture(2);
            var wallet = Wallet.Initial with { Gold = 500, Ore = 100, Essence = 40, RootCores = 3, MoonCrystals = 4 };
            var peerWallet = Wallet.Initial with { Gold = 200, Ore = 20 };
            var profile = Blob("legacy-contract-profile");
            // This tests the typed backfill protocol only. This opaque blob is deliberately not represented as a real UE SaveGame.
            var legacy = new CommitRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), 0, profile, wallet);
            var legacyAck = await Call(server, "/v1/server/commit", legacy, HttpStatusCode.OK);
            Check(legacyAck.GetProperty("revision").GetInt64() == 1,
                  "legacy v0 fixture seeded only through existing server commit");
            var unbackfilled = await Call(user, "/v1/me/inventory", null, HttpStatusCode.OK);
            Check(unbackfilled.GetProperty("playerId").GetGuid() == hero.PlayerId &&
                      unbackfilled.GetProperty("revision").GetInt64() == 1 &&
                      unbackfilled.GetProperty("profileHash").GetString() == BlobHash(profile) &&
                      unbackfilled.GetProperty("inventoryVersion").GetInt32() == 0 &&
                      unbackfilled.GetProperty("inventory").ValueKind == JsonValueKind.Null,
                  "legacy blob has explicit empty v0 canonical inventory before backfill");
            long revision = 1;
            var backfill = new CommitInventoryRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), revision, profile,
                                                      wallet, inventory);
            await AcceptInventory(options, server, user, backfill, "v0 to v1 atomic backfill");
            ++revision;
            await AssertJournal(options, hero.PlayerId, backfill.OperationId, null, inventory, "first backfill");
            var backfillFingerprint = await DatabaseFingerprint(options, hero.PlayerId);
            var repeatedBackfill = await Call(server, InventoryCommitRoute, backfill, HttpStatusCode.OK);
            Check(repeatedBackfill.GetProperty("revision").GetInt64() == revision &&
                      await DatabaseFingerprint(options, hero.PlayerId) == backfillFingerprint,
                  "backfill replay preserves stable UUIDs and all SQL rows");
            var oldReplay = await Call(server, "/v1/server/commit", legacy, HttpStatusCode.OK);
            Check(oldReplay.GetProperty("revision").GetInt64() == 1 &&
                      oldReplay.GetProperty("profileHash").GetString() == BlobHash(profile) &&
                      await DatabaseFingerprint(options, hero.PlayerId) == backfillFingerprint,
                  "old operation receipt survives typed inventory transition without mutation");
            await RejectUnchanged(options, server,
                                  legacy with { OperationId = Guid.NewGuid(), ExpectedRevision = revision,
                                                Wallet = wallet with { Gold = 499 } },
                                  HttpStatusCode.Conflict, "old v1 commit cannot mutate migrated inventory owner",
                                  owners, "/v1/server/commit");
            var peerRequest = new CommitInventoryRequest(peer.PlayerId, peer.LeaseId, Guid.NewGuid(), 0, Blob("peer"),
                                                         peerWallet, peerInventory);
            var missingRevision = JsonSerializer.SerializeToNode(peerRequest, Json)!.AsObject();

            // 접속 권한·저장 리비전·멱등 키 조건 검사
            if (!missingRevision.Remove("expectedRevision"))
            {
                throw new InvalidOperationException("Expected serialized revision field");
            }

            // 접속 권한·저장 리비전·멱등 키 처리
            await RejectUnchanged(options, server, missingRevision, HttpStatusCode.BadRequest,
                                  "missing zero expectedRevision is rejected rather than defaulted", owners);
            await AcceptInventory(options, server, other, peerRequest, "new account typed bootstrap");
            await AssertJournal(options, peer.PlayerId, peerRequest.OperationId, null, peerInventory,
                                "new typed bootstrap");
            Check(inventory.Items.Select(i => i.LocalId).SequenceEqual(peerInventory.Items.Select(i => i.LocalId)) &&
                      !inventory.Items.Select(i => i.InstanceId)
                           .Intersect(peerInventory.Items.Select(i => i.InstanceId))
                           .Any(),
                  "two owners may share local IDs while central UUIDs remain disjoint");
            await Call(anonymous, "/v1/me/inventory", null, HttpStatusCode.Unauthorized);
            await Call(server, "/v1/me/inventory", null, HttpStatusCode.Unauthorized);
            var candidate = new CommitInventoryRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), revision, profile,
                                                       wallet, inventory);
            await RejectUnchanged(options, user, candidate, HttpStatusCode.Unauthorized,
                                  "client token cannot commit inventory", owners);
            await RejectUnchanged(options, wrongKey, candidate, HttpStatusCode.Unauthorized,
                                  "wrong server key cannot commit inventory", owners);
            await RejectUnchanged(options, server, candidate with { LeaseId = peer.LeaseId }, HttpStatusCode.Conflict,
                                  "other owner's lease cannot commit", owners);
            var invalidSnapshots = new(string Name, InventorySnapshot Snapshot)[] {
                ("duplicate UUID across bag and stash",
                 ReplaceItem(inventory, 6, i => i with { InstanceId = inventory.Items[4].InstanceId })),
                ("duplicate local ID across bag and stash", ReplaceItem(inventory, 6, i => i with { LocalId = 5 })),
                ("duplicate inventory position", ReplaceItem(inventory, 5, i => i with { Position = 3 })),
                ("non-contiguous stash position", ReplaceItem(inventory, 6, i => i with { Position = 1 })),
                ("missing starter", inventory with { Items = inventory.Items.Where(i => i.LocalId != 1).ToArray() }),
                ("wrong-class equipped weapon",
                 inventory with { EquippedWeaponInstanceId = inventory.Items[0].InstanceId }),
                ("equipped item in stash", inventory with { EquippedArmorInstanceId = inventory.Items[5].InstanceId }),
                ("unknown equipped UUID", inventory with { EquippedArmorInstanceId = Guid.NewGuid() }),
                ("empty central UUID", ReplaceItem(inventory, 5, i => i with { InstanceId = Guid.Empty })),
                ("durability above maximum", ReplaceItem(inventory, 5, i => i with { Durability = 121 })),
                ("invalid grade durability",
                 ReplaceItem(inventory, 5, i => i with { MaxDurability = 100, Durability = 100 })),
                ("roll power mismatch", ReplaceItem(inventory, 5, i => i with { Power = 69 })),
                ("duplicate affix stat",
                 ReplaceItem(inventory, 5, i => i with { Affixes = [new ItemAffix(0, 2), new ItemAffix(0, 3)] })),
                ("affix out of range",
                 ReplaceItem(inventory, 5, i => i with { Affixes = [new ItemAffix(3, 16), new ItemAffix(0, 6)] })),
                ("null affix collection", ReplaceItem(inventory, 5, i => i with { Affixes = null! })),
                ("null item collection", inventory with { Items = null! }),
                ("unsupported snapshot version", inventory with { Version = 2 })
            };

            // 접속 권한·저장 리비전·멱등 키 순회
            foreach (var invalid in invalidSnapshots)
            {
                await RejectUnchanged(options, server,
                                      candidate with { OperationId = Guid.NewGuid(), Inventory = invalid.Snapshot },
                                      HttpStatusCode.BadRequest, invalid.Name, owners);
            }
            // Remove actual required zero-valued fields: null/invalid-value tests cannot detect silent defaults.

            // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
            var zeroHeroRequest =
                candidate with { OperationId = Guid.NewGuid(),
                                 Inventory = inventory with { HeroClass = 0, EquippedWeaponInstanceId =
                                                                                 inventory.Items[0].InstanceId } };
            var missingHeroClass = JsonSerializer.SerializeToNode(zeroHeroRequest, Json)!.AsObject();

            // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
            if (!missingHeroClass["inventory"]!.AsObject().Remove("heroClass"))
            {
                throw new InvalidOperationException("Expected serialized hero class field");
            }

            // 접속 권한·저장 리비전·멱등 키 처리
            await RejectUnchanged(options, server, missingHeroClass, HttpStatusCode.BadRequest,
                                  "missing zero snapshot heroClass is rejected rather than defaulted", owners);
            var missingDurabilityVersion =
                JsonSerializer.SerializeToNode(candidate with { OperationId = Guid.NewGuid() }, Json)!.AsObject();

            // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
            if (!missingDurabilityVersion["inventory"]!["items"]![5]!.AsObject().Remove("durabilityVersion"))
            {
                throw new InvalidOperationException("Expected serialized durability version field");
            }

            // 접속 권한·저장 리비전·멱등 키 처리
            await RejectUnchanged(options, server, missingDurabilityVersion, HttpStatusCode.BadRequest,
                                  "missing zero item durabilityVersion is rejected rather than defaulted", owners);
            var missingAffixStat =
                JsonSerializer.SerializeToNode(candidate with { OperationId = Guid.NewGuid() }, Json)!.AsObject();

            // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
            if (!missingAffixStat["inventory"]!["items"]![4]!["affixes"]![1]!.AsObject().Remove("stat"))
            {
                throw new InvalidOperationException("Expected serialized affix stat field");
            }

            // 접속 권한·저장 리비전·멱등 키 처리
            await RejectUnchanged(options, server, missingAffixStat, HttpStatusCode.BadRequest,
                                  "missing zero affix stat is rejected rather than defaulted", owners);
            await RejectUnchanged(
                options, server, candidate with { OperationId = Guid.NewGuid(), Wallet = wallet with { Gold = -1 } },
                HttpStatusCode.BadRequest, "invalid wallet leaves canonical inventory unchanged", owners);
            await RejectUnchanged(
                options, server, candidate with { OperationId = Guid.NewGuid(), Profile = profile + " " },
                HttpStatusCode.BadRequest, "invalid profile leaves canonical inventory unchanged", owners);
            await RejectUnchanged(
                options, server, backfill with { Inventory = ReplaceItem(inventory, 5, i => i with { Upgrade = 1 }) },
                HttpStatusCode.Conflict, "same operation with item-only body change conflicts", owners);
            await RejectUnchanged(
                options, server,
                peerRequest with { OperationId = Guid.NewGuid(), ExpectedRevision = 1,
                                   Inventory = ReplaceItem(
                                       peerInventory, 5, i => i with { InstanceId = inventory.Items[4].InstanceId }) },
                HttpStatusCode.Conflict, "cross-owner UUID theft conflicts without touching either owner", owners);
            await RejectUnchanged(options, server,
                                  candidate with { OperationId = Guid.NewGuid(),
                                                   Inventory = ReplaceItem(inventory, 5, i => i with { LocalId = 7 })
                                                       with { NextLocalId = 8 } },
                                  HttpStatusCode.Conflict, "existing UUID cannot change its local ID", owners);

            /// <summary>검증용 JSON 입력에 지정한 변경을 적용한다.</summary>
            // 접속 권한·저장 리비전·멱등 키 처리
            async Task Apply(InventorySnapshot next, Wallet nextWallet, string label)
            {
                // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
                var previous = inventory;
                var request = new CommitInventoryRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), revision,
                                                         Blob(label), nextWallet, next);
                await AcceptInventory(options, server, user, request, label);
                await AssertJournal(options, hero.PlayerId, request.OperationId, previous, next, label);
                inventory = next;
                wallet = nextWallet;
                profile = request.Profile;
                ++revision;
            }
            await Apply(ReplaceItem(inventory, 5, i => i with { Upgrade = 1 }),
                        wallet with { Gold = wallet.Gold - 30, Ore = wallet.Ore - 2 },
                        "upgrade with exact resource snapshot");
            await Apply(ReplaceItem(inventory, 5, i => i with { Durability = 115 }), wallet,
                        "wear keeps rolled stats and UUID");
            await Apply(ReplaceItem(inventory, 5, i => i with { Durability = 120 }),
                        wallet with { Gold = wallet.Gold - 20, Ore = wallet.Ore - 1 },
                        "repair with exact resource snapshot");
            await Apply(ReplaceItem(inventory, 6, i => Change(i, 0, 5))
                            with { EquippedArmorInstanceId = inventory.Items.Single(i => i.LocalId == 6).InstanceId },
                        wallet, "withdraw and equip armor without changing UUID");
            var toStash =
                inventory with { Items = inventory.Items.Where(i => i.LocalId != 5)
                                             .Select((item, position) => Change(item, 0, position))
                                             .Append(Change(inventory.Items.Single(i => i.LocalId == 5), 1, 0))
                                             .ToArray() };
            await Apply(toStash, wallet, "deposit weapon preserves identity and contiguous positions");
            var reorderedAffixes =
                ReplaceItem(inventory, 5, i => i with { Affixes = [new ItemAffix(0, 6), new ItemAffix(3, 15)] });
            await Apply(reorderedAffixes, wallet, "affix array order is preserved exactly");
            await Apply(ReplaceItem(inventory, 5, i => Change(i, 0, 5)), wallet, "withdraw weapon preserves UUID");
            var soldItem = inventory.Items.Single(i => i.LocalId == 5);
            await Apply(inventory with { Items = inventory.Items.Where(i => i.LocalId != 5).ToArray() },
                        wallet with { Gold = wallet.Gold + 65 },
                        "sell removes active item and credits wallet atomically");
            Check(Convert.ToBoolean(await Sql(
                      options, "SELECT retired FROM inventory_items WHERE instance_id=$1 AND owner_player_id=$2",
                      soldItem.InstanceId, hero.PlayerId)),
                  "sale retains retired owner UUID tombstone");
            var retiredSnapshot =
                inventory with { Items =
                                     inventory.Items.Append(Change(soldItem, 0, inventory.Items.Length)).ToArray() };
            candidate = new(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), revision, profile, wallet, retiredSnapshot);
            await RejectUnchanged(options, server, candidate, HttpStatusCode.Conflict, "retired UUID cannot resurrect",
                                  owners);
            await RejectUnchanged(
                options, server,
                candidate with { OperationId = Guid.NewGuid(),
                                 Inventory =
                                     ReplaceItem(retiredSnapshot, 5, i => i with { InstanceId = Guid.NewGuid() }) },
                HttpStatusCode.Conflict, "retired local ID cannot be reused with fresh UUID", owners);
            await RejectUnchanged(
                options, server,
                peerRequest with { OperationId = Guid.NewGuid(), ExpectedRevision = 1,
                                   Inventory = ReplaceItem(peerInventory, 5,
                                                           i => i with { InstanceId = soldItem.InstanceId }) },
                HttpStatusCode.Conflict, "retired UUID cannot be stolen by another owner", owners);
            var bought = PurchasedWeapon(7, inventory.Items.Length, 1);
            await Apply(inventory with { NextLocalId = 8, Items = inventory.Items.Append(bought).ToArray() },
                        wallet with { Gold = wallet.Gold - 100 },
                        "new purchase uses monotonic local ID and fresh UUID");
            // Remove the newest item so a lower nextLocalId is structurally valid, yet forbidden by history.
            await Apply(inventory with { Items = inventory.Items.Where(i => i.LocalId != 7).ToArray() },
                        wallet with { Gold = wallet.Gold + 25 }, "second sale preserves nextLocalId high-water mark");
            await RejectUnchanged(options, server,
                                  new CommitInventoryRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), revision,
                                                             profile, wallet, inventory with { NextLocalId = 7 }),
                                  HttpStatusCode.Conflict, "nextLocalId high-water mark cannot move backward", owners);
            var active = PurchasedWeapon(8, inventory.Items.Length, 1);
            await Apply(inventory with { NextLocalId = 9, Items = inventory.Items.Append(active).ToArray() },
                        wallet with { Gold = wallet.Gold - 100 }, "purchase after tombstones never reuses local IDs");

            // 접속 권한·저장 리비전·멱등 키 처리
            await Call(server, "/v1/server/heartbeat", new LeaseRequest(hero.PlayerId, hero.LeaseId),
                       HttpStatusCode.OK);
            await Call(server, "/v1/server/heartbeat", new LeaseRequest(peer.PlayerId, peer.LeaseId),
                       HttpStatusCode.OK);
            var raceA =
                new CommitInventoryRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), revision, Blob("race-A"),
                                           wallet with { Gold = wallet.Gold - 30, Ore = wallet.Ore - 2 },
                                           ReplaceItem(inventory, 8, i => i with { Upgrade = 1 }));
            var raceB = raceA with { OperationId = Guid.NewGuid(), Profile = Blob("race-B"),
                                     Wallet = wallet with { Gold = wallet.Gold - 120, Ore = wallet.Ore - 4 },
                                     Inventory = ReplaceItem(inventory, 8, i => i with { Upgrade = 2 }) };
            var responses = await Task.WhenAll(server.PostAsJsonAsync(InventoryCommitRoute, raceA),
                                               server.PostAsJsonAsync(InventoryCommitRoute, raceB));
            CommitInventoryRequest winner;

            // 접속 권한·저장 리비전·멱등 키 처리
            try
            {
                Check(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1 &&
                          responses.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1,
                      "inventory CAS race has exactly one winner and one conflict");
                winner = responses[0].StatusCode == HttpStatusCode.OK ? raceA : raceB;
                var receipt = await(responses[0].StatusCode == HttpStatusCode.OK ? responses[0] : responses[1])
                                  .Content.ReadFromJsonAsync<CommitInventoryResponse>(Json);
                Check(receipt is not null && receipt.Revision == revision + 1 &&
                          receipt.ProfileHash == BlobHash(winner.Profile) && receipt.InventoryVersion == 1,
                      "inventory race winner ACK identifies complete winner snapshot");
            }
            finally
            {
                foreach (var response in responses)
                {
                    response.Dispose();
                }
            }

            // 접속 권한·저장 리비전·멱등 키 처리
            await AssertJournal(options, hero.PlayerId, winner.OperationId, inventory, winner.Inventory, "race winner");
            inventory = winner.Inventory;
            wallet = winner.Wallet;
            profile = winner.Profile;
            ++revision;
            await AssertInventoryAccount(options, user, hero.PlayerId, revision, BlobHash(profile), wallet, inventory,
                                         "race has no mixed winner fields");
            await RejectUnchanged(options, server, winner == raceA ? raceB : raceA, HttpStatusCode.Conflict,
                                  "race loser remains rejected", owners);
            var beforeReplay = await DatabaseFingerprint(options, hero.PlayerId);
            var replayAck = await Call(server, InventoryCommitRoute, winner, HttpStatusCode.OK);
            Check(replayAck.GetProperty("revision").GetInt64() == revision &&
                      await DatabaseFingerprint(options, hero.PlayerId) == beforeReplay,
                  "race winner replay does not duplicate items or any accounting rows");
            await RejectUnchanged(
                options, server, winner with { Inventory = ReplaceItem(inventory, 8, i => i with { Durability = 79 }) },
                HttpStatusCode.Conflict, "post-race item-only operation collision rejected", owners);

            // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
            var createdByFault = PurchasedWeapon(9, inventory.Items.Length, 1);
            var faultInventory = ReplaceItem(inventory, 8, i => i with { Upgrade = 3 }) with {
                NextLocalId = 10,
                Items = ReplaceItem(inventory, 8, i => i with { Upgrade = 3 }).Items.Append(createdByFault).ToArray()
            };
            var fault =
                new CommitInventoryRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), revision, Blob("journal-fault"),
                                           wallet with { Gold = wallet.Gold + 13 }, faultInventory);
            // Only this UUID-named isolated test schema is modified; no production schema or profile is touched.
            await Sql(
                options,
                $"ALTER TABLE inventory_journal ADD CONSTRAINT inventory_atomic_fault CHECK(player_id <> '{hero.PlayerId:D}'::uuid OR revision <> {revision + 1})");

            // SQL 명령과 조회 결과 처리
            try
            {
                await RejectUnchanged(options, server, fault, HttpStatusCode.ServiceUnavailable,
                                      "last journal insert failure rolls entire transaction back", owners);
            }
            finally
            {
                await Sql(options, "ALTER TABLE inventory_journal DROP CONSTRAINT inventory_atomic_fault");
            }

            // 접속 권한·저장 리비전·멱등 키 처리
            Check(Convert.ToInt64(await Sql(options, "SELECT count(*) FROM inventory_items WHERE instance_id=$1",
                                            createdByFault.InstanceId)) == 0,
                  "failed transaction leaves no new UUID or tombstone");
            await AcceptInventory(options, server, user, fault,
                                  "same failed operation succeeds once after fault removed");
            await AssertJournal(options, hero.PlayerId, fault.OperationId, inventory, faultInventory,
                                "successful fault retry");
            inventory = faultInventory;
            wallet = fault.Wallet;
            profile = fault.Profile;
            ++revision;
            await AssertInventoryAccount(options, other, peer.PlayerId, 1, BlobHash(peerRequest.Profile), peerWallet,
                                         peerInventory, "other owner unchanged throughout mutations and race");
            await RejectUnchanged(options, server,
                                  legacy with { OperationId = Guid.NewGuid(), ExpectedRevision = revision,
                                                Profile = profile, Wallet = wallet },
                                  HttpStatusCode.Conflict,
                                  "old mutation route remains blocked after all inventory operations", owners,
                                  "/v1/server/commit");

            // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
            var beforeHistoricalReplay = await DatabaseFingerprint(options, hero.PlayerId);
            var historical = await Call(server, InventoryCommitRoute, backfill, HttpStatusCode.OK);
            Check(historical.GetProperty("revision").GetInt64() == 2 &&
                      historical.GetProperty("profileHash").GetString() == BlobHash(backfill.Profile) &&
                      await DatabaseFingerprint(options, hero.PlayerId) == beforeHistoricalReplay,
                  "historical backfill receipt does not resurrect sold items or revert current inventory");
            var originalReceipt = await Call(server, "/v1/server/commit", legacy, HttpStatusCode.OK);
            Check(originalReceipt.GetProperty("revision").GetInt64() == 1 &&
                      originalReceipt.GetProperty("profileHash").GetString() == BlobHash(legacy.Profile) &&
                      await DatabaseFingerprint(options, hero.PlayerId) == beforeHistoricalReplay,
                  "historical opaque commit receipt remains read-only after later typed mutations");
            await Call(server, "/v1/server/release", new LeaseRequest(hero.PlayerId, hero.LeaseId), HttpStatusCode.OK);
            var newTicket =
                (await Call(user, "/v1/join-tickets", new {}, HttpStatusCode.OK)).GetProperty("ticket").GetString();
            var restored = await Call(server, "/v1/server/claim",
                                      new { ticket = newTicket, serverId = "inventory-reconnect" }, HttpStatusCode.OK);
            var restoredLease = restored.GetProperty("leaseId").GetGuid();
            Check(restoredLease != hero.LeaseId && restored.GetProperty("revision").GetInt64() == revision &&
                      restored.GetProperty("profile").GetString() == profile &&
                      restored.GetProperty("profileHash").GetString() == BlobHash(profile) &&
                      restored.GetProperty("wallet").Deserialize<Wallet>(Json) == wallet &&
                      restored.GetProperty("inventoryVersion").GetInt32() == 1 &&
                      SameJson(restored.GetProperty("inventory").Deserialize<InventorySnapshot>(Json), inventory),
                  "new claim restores exact profile/wallet/ordered inventory with new lease");
            var afterReconnect = await DatabaseFingerprint(options, hero.PlayerId);
            var priorLeaseReceipt = await Call(server, InventoryCommitRoute, fault, HttpStatusCode.OK);
            Check(priorLeaseReceipt.GetProperty("revision").GetInt64() == revision &&
                      await DatabaseFingerprint(options, hero.PlayerId) == afterReconnect,
                  "exact typed receipt replays read-only after its old lease was replaced");
            await RejectUnchanged(options, server,
                                  fault with { OperationId = Guid.NewGuid(), ExpectedRevision = revision },
                                  HttpStatusCode.Conflict, "old inventory lease is fenced after reconnect", owners);
            await Call(server, "/v1/server/release", new LeaseRequest(hero.PlayerId, restoredLease), HttpStatusCode.OK);
            await Call(server, "/v1/server/release", new LeaseRequest(peer.PlayerId, peer.LeaseId), HttpStatusCode.OK);
            var heroCheckpoint =
                await CaptureInventoryCheckpoint(options, hero.PlayerId, revision, profile, wallet, inventory);
            var peerCheckpoint = await CaptureInventoryCheckpoint(options, peer.PlayerId, 1, peerRequest.Profile,
                                                                  peerWallet, peerInventory);
            Check(heroCheckpoint.Operations == revision && heroCheckpoint.LedgerRows == revision &&
                      heroCheckpoint.JournalRows == revision - 1 && peerCheckpoint.Operations == 1 &&
                      peerCheckpoint.LedgerRows == 1 && peerCheckpoint.JournalRows == 1,
                  "only successful commits produce exactly one operation/ledger and typed journal row");

            // SQL 명령과 조회 결과 순회
            foreach (var expected in new[] { heroCheckpoint, peerCheckpoint })
            {
                // 원시 SQL 문자열의 본문과 공백을 그대로 보존한다.
                // clang-format off
                var ledgerText = (string?)await Sql(options, """
                    SELECT jsonb_build_object('gold',COALESCE(sum(gold_delta),0),'ore',COALESCE(sum(ore_delta),0),
                      'essence',COALESCE(sum(essence_delta),0),'rootCores',COALESCE(sum(root_cores_delta),0),
                      'moonCrystals',COALESCE(sum(moon_crystals_delta),0),'healthPotions',3+COALESCE(sum(health_potions_delta),0),
                      'manaPotions',2+COALESCE(sum(mana_potions_delta),0))::text FROM economy_ledger WHERE player_id=$1
                    """, expected.PlayerId);
                // clang-format on
                Check(ledgerText is not null && JsonSerializer.Deserialize<Wallet>(ledgerText, Json) == expected.Wallet,
                      "all seven ledger resources reconcile to canonical wallet");
            }

            checkpoint = new(1, options.Schema, [heroCheckpoint, peerCheckpoint]);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        // 아이템·장비 옵션·인벤토리와 창고 처리
        await VerifyInventoryPersistence(options, checkpoint ?? throw new InvalidOperationException(
                                                                    "Inventory suite did not produce a checkpoint."));
        await WriteNew(Path.Combine(output, "inventory-persistence-checkpoint.json"), checkpoint);
        await WriteNew(Path.Combine(output, "inventory-integration.json"), new {
            status = "passed", checkedUtc = DateTime.UtcNow, options.Schema, checks = Checks.Skip(firstCheck).ToArray(),
            originalLegacyCheckCount = firstCheck, legacySchemaMigration, inventoryCheckpointFormat = 1,
            databaseRestartObserved = false,
            scope =
                "Real HTTP and SQL in a new fable_test UUID schema. Opaque blobs are backend contract fixtures, not actual UE SaveGame conversion evidence. No gameplay memory/reward injection or real account mutation."
        });
    }
}
