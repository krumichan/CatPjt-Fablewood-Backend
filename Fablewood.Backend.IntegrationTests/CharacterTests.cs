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
    private const string CharacterRoute = "/v1/server/commit-character";

    /// <summary>플레이어별 캐릭터 요약의 재시작 전 기준 상태.</summary>
    private sealed record CharacterPlayerCheckpoint(InventoryPlayerCheckpoint Inventory, CharacterSummary Summary,
                                                    long CharacterJournalRows, string CharacterFingerprint);

    /// <summary>캐릭터 요약 영구 저장 검증의 재시작 전 기준 상태.</summary>
    private sealed record CharacterCheckpoint(int FormatVersion, string Schema, CharacterPlayerCheckpoint[] Players);

    /// <summary>SQL에 저장된 캐릭터 요약을 검증용 JSON으로 읽는다.</summary>
    private static async Task<CharacterSummary?> ReadSqlSummary(BackendOptions options, Guid player)
    {
        var json = (string?)await Sql(
            options,
            "SELECT jsonb_build_object('version',version,'heroClass',hero_class,'level',level,'experience',experience,'classTier',class_tier)::text FROM character_summaries WHERE player_id=$1",
            player);
        return json is null ? null : JsonSerializer.Deserialize<CharacterSummary>(json, Json);
    }

    /// <summary>상태 불변 검증에 사용할 SQL 캐릭터 요약·이력 지문을 만든다.</summary>
    private static async Task<string> CharacterFingerprint(BackendOptions options, Guid player)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var legacy = await DatabaseFingerprint(options, player);
        // 원시 SQL 문자열의 본문과 공백을 그대로 보존한다.
        // clang-format off
        var projection = (string?)await Sql(options, """
            SELECT jsonb_build_object(
              'summary',(SELECT to_jsonb(s) FROM character_summaries s WHERE player_id=$1),
              'journal',(SELECT COALESCE(jsonb_agg(to_jsonb(j) ORDER BY operation_id),'[]'::jsonb) FROM character_journal j WHERE player_id=$1),
              'releases',(SELECT COALESCE(jsonb_agg(to_jsonb(r) ORDER BY lease_id),'[]'::jsonb) FROM released_leases r WHERE player_id=$1))::text
            """, player) ??
                         throw new InvalidOperationException("Character fingerprint missing.");
        // clang-format on

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(legacy + "\n" + projection)));
    }

    /// <summary>거부된 캐릭터 요청이 SQL 상태와 변경 이력을 보존하는지 확인한다.</summary>
    private static async Task RejectCharacterUnchanged(BackendOptions options, HttpClient client, object request,
                                                       HttpStatusCode status, string label, Guid[] owners,
                                                       string route = CharacterRoute)
    {
        var before = new List<string>();

        // 같은 규칙을 적용할 대상을 순회한다.
        foreach (var owner in owners)
        {
            before.Add(await CharacterFingerprint(options, owner));
        }

        await Call(client, route, request, status);
        var after = new List<string>();

        // 같은 규칙을 적용할 대상을 순회한다.
        foreach (var owner in owners)
        {
            after.Add(await CharacterFingerprint(options, owner));
        }

        Check(before.SequenceEqual(after), label + ": all owner rows and projections unchanged");
    }

    /// <summary>공개 API와 SQL의 캐릭터 요약이 기대값과 같은지 확인한다.</summary>
    private static async Task AssertCharacter(BackendOptions options, HttpClient user, string name,
                                              CommitCharacterRequest request)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var actual = await Call(user, "/v1/me/character", null, HttpStatusCode.OK);
        Check(actual.GetProperty("name").GetString() == name &&
                  actual.GetProperty("playerId").GetGuid() == request.PlayerId &&
                  actual.GetProperty("revision").GetInt64() == request.ExpectedRevision + 1 &&
                  actual.GetProperty("profileHash").GetString() == BlobHash(request.Profile) &&
                  actual.GetProperty("inventoryVersion").GetInt32() == 1 &&
                  actual.GetProperty("wallet").Deserialize<Wallet>(Json) == request.Wallet &&
                  SameJson(actual.GetProperty("inventory").Deserialize<InventorySnapshot>(Json), request.Inventory) &&
                  actual.GetProperty("summary").Deserialize<CharacterSummary>(Json) == request.Summary,
              name + ": own character projection/wallet/inventory/revision/hash exact");
        Check(!actual.TryGetProperty("profile", out _) && !actual.TryGetProperty("leaseId", out _) &&
                  !actual.TryGetProperty("password", out _) && actual.EnumerateObject().Count() == 9,
              "character endpoint exposes only the nine documented fields");
        Check(await ReadSqlSummary(options, request.PlayerId) == request.Summary,
              "independent canonical summary columns exact");
        await AssertBlobWallet(options, request.PlayerId, request.ExpectedRevision + 1, BlobHash(request.Profile),
                               request.Wallet, name);
        Check(SameJson(await ReadSqlInventory(options, request.PlayerId), request.Inventory),
              "character commit canonical inventory exact");
    }

    /// <summary>캐릭터 요청의 성공 응답과 저장 결과를 확인한다.</summary>
    private static async Task AcceptCharacter(BackendOptions options, HttpClient server, HttpClient user, string name,
                                              CommitCharacterRequest request)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var ack = await Call(server, CharacterRoute, request, HttpStatusCode.OK);
        Check(ack.GetProperty("revision").GetInt64() == request.ExpectedRevision + 1 &&
                  ack.GetProperty("profileHash").GetString() == BlobHash(request.Profile) &&
                  ack.GetProperty("inventoryVersion").GetInt32() == 1 &&
                  ack.GetProperty("summaryVersion").GetInt32() == 1,
              "character ACK has both version gates and exact revision/hash");
        await AssertCharacter(options, user, name, request);
    }

    /// <summary>캐릭터 요약 변경 이력 결과가 기대한 저장 상태와 같은지 확인한다.</summary>
    private static async Task AssertSummaryJournal(BackendOptions options, CommitCharacterRequest request,
                                                   CharacterSummary? before)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var json =
            (string?)await Sql(
                options,
                "SELECT jsonb_build_object('before',before_summary,'after',after_summary,'revision',revision)::text FROM character_journal WHERE player_id=$1 AND operation_id=$2",
                request.PlayerId, request.OperationId) ??
            throw new InvalidOperationException("Summary journal missing.");
        using var doc = JsonDocument.Parse(json);
        var prior = doc.RootElement.GetProperty("before");
        Check((before is null ? prior.ValueKind == JsonValueKind.Null
                              : prior.Deserialize<CharacterSummary>(Json) == before) &&
                  doc.RootElement.GetProperty("after").Deserialize<CharacterSummary>(Json) == request.Summary &&
                  doc.RootElement.GetProperty("revision").GetInt64() == request.ExpectedRevision + 1,
              "summary journal exact before/after/revision");
    }

    /// <summary>캐릭터 확인 지점 기준 상태를 기록한다.</summary>
    private static async Task<CharacterPlayerCheckpoint> CaptureCharacterCheckpoint(BackendOptions options,
                                                                                    CommitCharacterRequest request)
    {
        var inventory = await CaptureInventoryCheckpoint(options, request.PlayerId, request.ExpectedRevision + 1,
                                                         request.Profile, request.Wallet, request.Inventory);
        return new(inventory, request.Summary,
                   Convert.ToInt64(await Sql(options, "SELECT count(*) FROM character_journal WHERE player_id=$1",
                                             request.PlayerId)),
                   await CharacterFingerprint(options, request.PlayerId));
    }

    /// <summary>재시작 전후의 캐릭터 요약과 체크포인트가 같은지 확인한다.</summary>
    private static async Task VerifyCharacterPersistence(BackendOptions options, CharacterCheckpoint checkpoint)
    {
        Check(checkpoint.FormatVersion == 1 && checkpoint.Schema == options.Schema && checkpoint.Players.Length == 2,
              "character persistence checkpoint schema/two owners valid");
        foreach (var player in checkpoint.Players)
        {
            // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
            var expected = player.Inventory;
            await Verify(options, new(checkpoint.Schema, expected.PlayerId, expected.Revision, expected.ProfileHash,
                                      expected.Wallet, expected.Operations, expected.LedgerRows));
            Check(SameJson(await ReadSqlInventory(options, expected.PlayerId), expected.Inventory),
                  "persistent character ordered inventory exact");
            Check(await ReadSqlSummary(options, expected.PlayerId) == player.Summary,
                  "persistent character summary exact");
            Check(await CharacterFingerprint(options, expected.PlayerId) == player.CharacterFingerprint,
                  "persistent character complete rows, release receipts and journals exact");
            Check(Convert.ToInt64(await Sql(options, "SELECT count(*) FROM character_journal WHERE player_id=$1",
                                            expected.PlayerId)) == player.CharacterJournalRows,
                  "persistent character journal count exact");
        }
    }

    /// <summary>서버 목록 설정 값과 허용 조건을 검사한다.</summary>
    private static void ValidateCatalogOptions(BackendOptions options)
    {
        // 기대값과 상태 보존 검증 순회
        foreach (var address in new[] { "127.0.0.1:7821", "game.example.com:7777", "192.168.0.1:65535", "localhost:1",
                                        "game-server.example:00777" })
        {
            (options with { GameAddress = address }).ValidateCatalog();
            Check(true, "configured game address accepts plain host/port");
        }

        // 같은 규칙을 적용할 대상을 순회한다.
        foreach (var address in new[] { "http://127.0.0.1:7821",
                                        "127.0.0.1:7821?listen",
                                        "127.0.0.1:7821#x",
                                        "x@host:7821",
                                        "x:0",
                                        "x:65536",
                                        "x:7 8",
                                        "host/name:7821",
                                        "[::1%3]:7821",
                                        "-host:7821",
                                        "host..test:7821",
                                        "host:7821\n",
                                        "[::1]:7821",
                                        "127.1:7821",
                                        "127.00.0.1:7821",
                                        "999.0.0.1:7821",
                                        "1.2.3.256:7821",
                                        "123:7821",
                                        "host-.example:7821",
                                        "host.-example:7821",
                                        "host.:7821",
                                        ".host:7821",
                                        new string('a', 64) + ":7821",
                                        new string('a', 63) + "." + new string('b', 63) + "." + new string('c', 63) +
                                            "." + new string('d', 62) + ":7821" })
        {
            bool rejected = false;
            try
            {
                (options with { GameAddress = address }).ValidateCatalog();
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }
            Check(rejected, "game address rejects travel options or malformed host/port");
        }

        bool badName = false;

        // 이 단계에서 서버 목록 설정 값과 허용 조건을 검사한다.
        try
        {
            (options with { GameServerName = "unsafe\nname" }).ValidateCatalog();
        }
        catch (InvalidOperationException)
        {
            badName = true;
        }

        // 기대값과 상태 보존 검증 처리
        Check(badName, "catalog display name rejects control characters");

        bool badId = false;

        // 이 단계에서 서버 목록 설정 값과 허용 조건을 검사한다.
        try
        {
            (options with { GameServerId = "bad?id" }).ValidateCatalog();
        }
        catch (ApiError)
        {
            badId = true;
        }

        Check(badId, "catalog server id uses strict server identifier rules");
    }

    /// <summary>이전 인벤토리 스키마가 캐릭터 요약 스키마로 보존 이관되는지 검증한다.</summary>
    private static async Task<object> VerifySchemaTwoMigration(BackendOptions source, Guid player)
    {
        var target = Isolated(source, "fable_test_" + Guid.NewGuid().ToString("N"));
        await Sql(target, $"CREATE SCHEMA \"{target.Schema}\"");

        // SQL 명령과 조회 결과 순회
        foreach (var file in new[] { "001_initial.sql", "002_inventory.sql" })
        {
            using var stream =
                typeof(SqlStore).Assembly.GetManifestResourceStream("Fablewood.Backend.Migrations." + file) ??
                throw new InvalidOperationException("Prior migration missing.");
            using var reader = new StreamReader(stream);
            await Sql(target, await reader.ReadToEndAsync());
        }
        // Copy synthetic rows from this run only; both schema names were validated by Isolated.

        // 데이터베이스 트랜잭션 처리
        await using (var connection = new NpgsqlConnection(target.ConnectionString))
        {
            // 데이터베이스 트랜잭션 처리
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();

            /// <summary>이관 검증에 필요한 이전 스키마의 SQL 행을 현재 테스트 스키마로 복사한다.</summary>
            async Task Copy(string table, string predicate)
            {
                await using var cmd = new NpgsqlCommand(
                    $"INSERT INTO {table} SELECT * FROM \"{source.Schema}\".{table} WHERE {predicate}", connection,
                    transaction);
                cmd.Parameters.AddWithValue(player);
                await cmd.ExecuteNonQueryAsync();
            }
            await Copy("accounts", "player_id=$1");
            // Item ownership is immediate; equipped-item references defer until this transaction commits.
            await Copy("profiles", "player_id=$1");
            await Copy("inventory_items", "owner_player_id=$1");
            await Copy(
                "inventory_affixes",
                $"instance_id IN (SELECT instance_id FROM \"{source.Schema}\".inventory_items WHERE owner_player_id=$1)");
            await Copy("operations", "player_id=$1");
            await Copy("economy_ledger", "player_id=$1");
            await Copy("inventory_journal", "player_id=$1");
            await transaction.CommitAsync();
        }
        var before = await DatabaseFingerprint(target, player);
        Check(Convert.ToInt32(await Sql(target, "SELECT max(version) FROM schema_migrations")) == 2,
              "real schema-two typed fixture starts before summary migration");

        var legacySession = Rules.Token();
        var legacyTicket = Rules.Token();
        await Sql(
            target,
            "INSERT INTO sessions(token_hash,player_id,expires_at) VALUES($1,$2,clock_timestamp()+interval '20 minutes')",
            Rules.TokenHash(legacySession), player);
        await Sql(
            target,
            "INSERT INTO join_tickets(token_hash,player_id,expires_at) VALUES($1,$2,clock_timestamp()+interval '180 seconds')",
            Rules.TokenHash(legacyTicket), player);
        var ticketBefore =
            (string?)await Sql(target, "SELECT (to_jsonb(t))::text FROM join_tickets t WHERE token_hash=$1",
                               Rules.TokenHash(legacyTicket));
        await using (var store = new SqlStore(target))
        {
            await store.Migrate(CancellationToken.None);
            await store.Migrate(CancellationToken.None);
        }
        Check(Convert.ToInt32(await Sql(target, "SELECT max(version) FROM schema_migrations")) == 3 &&
                  before == await DatabaseFingerprint(target, player),
              "schema two to three repeated migration preserves all original blob/wallet/items/operations/journals");
        Check(await ReadSqlSummary(target, player) is null &&
                  Convert.ToInt64(await Sql(target, "SELECT count(*) FROM character_journal")) == 0,
              "migration leaves unknown legacy level null without fabricated summary");
        Check(ticketBefore ==
                  (string?)await Sql(
                      target, "SELECT (to_jsonb(t)-'session_token_hash')::text FROM join_tickets t WHERE token_hash=$1",
                      Rules.TokenHash(legacyTicket)),
              "migration retains original issued ticket fields and expiry");
        await Sql(target,
                  "UPDATE profiles SET lease_id=NULL,lease_server=NULL,lease_expires_at=NULL WHERE player_id=$1",
                  player);
        await using (var store = new SqlStore(target))
        {
            var claim = await store.Claim(new(legacyTicket, "legacy-ticket-migration"), CancellationToken.None);
            Check(claim.PlayerId == player && claim.Summary is null && claim.InventoryVersion == 1,
                  "pre-migration unlinked ticket retains original one-use claim compatibility");
            await store.Release(new(player, claim.LeaseId), CancellationToken.None);
        }

        return new { schema = target.Schema, sourceSchemaVersion = 2, targetSchemaVersion = 3, playerId = player,
                     originalRowsSha256 = before };
    }

    /// <summary>격리 계정으로 캐릭터 요약 계약·충돌·영구 저장 회귀 검증을 실행한다.</summary>
    private static async Task RunCharacterSuite(BackendOptions options, string output)
    {
        int firstCheck = Checks.Count;
        ValidateCatalogOptions(options);

        var app = BackendApplication.Build(options);
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        CharacterCheckpoint? checkpoint = null;
        object? migration = null;

        // HTTP 요청·응답과 연결 상태 처리
        try
        {
            // HTTP 요청·응답과 연결 상태에 필요한 값 준비
            var address =
                app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var anonymous = Client(address);
            using var server = Client(address, options.ServerKey);
            using var wrongKey = Client(address, "invalid-server-key");
            await Call(anonymous, "/v1/servers", null, HttpStatusCode.Unauthorized);
            await Call(anonymous, "/v1/me/character", null, HttpStatusCode.Unauthorized);
            var hero = await CreateInventoryActor(anonymous, server, address, "character_hero", 1);
            var peer = await CreateInventoryActor(anonymous, server, address, "character_peer", 2);
            using var user = hero.Client;
            using var other = peer.Client;
            var owners = new[] { hero.PlayerId, peer.PlayerId };
            var catalog = await Call(user, "/v1/servers", null, HttpStatusCode.OK);
            var listed = catalog.GetProperty("servers").EnumerateArray().Single();
            Check(listed.EnumerateObject().Count() == 3 &&
                      listed.GetProperty("id").GetString() == options.GameServerId &&
                      listed.GetProperty("name").GetString() == options.GameServerName &&
                      listed.GetProperty("address").GetString() == options.GameAddress,
                  "catalog exposes only configured id/name/address without claiming runtime health");
            var empty = await Call(user, "/v1/me/character", null, HttpStatusCode.OK);
            Check(empty.GetProperty("summary").ValueKind == JsonValueKind.Null &&
                      empty.GetProperty("inventory").ValueKind == JsonValueKind.Null &&
                      empty.GetProperty("initialClass").GetInt32() == 1 &&
                      empty.GetProperty("playerId").GetGuid() == hero.PlayerId,
                  "new account has no invented character level or inventory");
            var inventory = InventoryFixture(1);
            var peerInventory = InventoryFixture(2);
            var wallet = Wallet.Initial with { Gold = 500, Ore = 60 };
            var blob = Blob("summary-legacy");
            var opaque = new CommitRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), 0, blob, wallet);
            await Call(server, "/v1/server/commit", opaque, HttpStatusCode.OK);
            var legacy =
                new CommitInventoryRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), 1, blob, wallet, inventory);
            await AcceptInventory(options, server, user, legacy, "pre-summary typed account");
            var beforeSummary = await Call(user, "/v1/me/character", null, HttpStatusCode.OK);
            Check(beforeSummary.GetProperty("summary").ValueKind == JsonValueKind.Null &&
                      beforeSummary.GetProperty("profileHash").GetString() == BlobHash(blob),
                  "existing typed blob exposes unknown level until legitimate server backfill");
            migration = await VerifySchemaTwoMigration(options, hero.PlayerId);
            var first = new CommitCharacterRequest(hero.PlayerId, hero.LeaseId, Guid.NewGuid(), 2, blob, wallet,
                                                   inventory, new(1, 1, 4, 90, 3));
            var peerFirst =
                new CommitCharacterRequest(peer.PlayerId, peer.LeaseId, Guid.NewGuid(), 0, Blob("peer-summary"),
                                           Wallet.Initial, peerInventory, new(1, 2, 1, 0, 1));
            await RejectCharacterUnchanged(options, anonymous, first, HttpStatusCode.Unauthorized,
                                           "anonymous cannot commit summary", owners);
            await RejectCharacterUnchanged(options, user, first, HttpStatusCode.Unauthorized,
                                           "client bearer cannot commit summary", owners);
            await RejectCharacterUnchanged(options, wrongKey, first, HttpStatusCode.Unauthorized,
                                           "wrong server key cannot commit summary", owners);
            await AcceptCharacter(options, server, user, "character_hero", first);
            await AssertSummaryJournal(options, first, null);
            await AcceptCharacter(options, server, other, "character_peer", peerFirst);
            await AssertSummaryJournal(options, peerFirst, null);
            Check(first.Profile == legacy.Profile && first.Summary.Version == 1,
                  "first summary backfill commits even when original blob SHA is unchanged");
            var afterFirst = await CharacterFingerprint(options, hero.PlayerId);
            var replay = await Call(server, CharacterRoute, first, HttpStatusCode.OK);
            Check(replay.GetProperty("revision").GetInt64() == 3 &&
                      await CharacterFingerprint(options, hero.PlayerId) == afterFirst,
                  "exact character replay is read-only and does not duplicate any journal");
            await RejectCharacterUnchanged(
                options, server, first with { Summary = first.Summary with { Experience = 91 } },
                HttpStatusCode.Conflict, "same operation different summary cannot reuse receipt", owners);
            var validNext =
                first with { ExpectedRevision = 3, OperationId = Guid.NewGuid(), Profile = Blob("summary-next") };
            await RejectCharacterUnchanged(options, server, validNext with { LeaseId = peer.LeaseId },
                                           HttpStatusCode.Conflict, "another owner lease cannot write character",
                                           owners);
            await RejectCharacterUnchanged(options, server, validNext with { ExpectedRevision = 2 },
                                           HttpStatusCode.Conflict, "stale expected revision fenced", owners);
            var invalid = new CharacterSummary?[] { null,
                                                    new(0, 1, 4, 90, 3),
                                                    new(2, 1, 4, 90, 3),
                                                    new(1, -1, 4, 90, 3),
                                                    new(1, 3, 4, 90, 3),
                                                    new(1, 2, 4, 90, 3),
                                                    new(1, 1, 0, 0, 1),
                                                    new(1, 1, 21, 0, 1),
                                                    new(1, 1, 4, -1, 3),
                                                    new(1, 1, 4, 400, 3),
                                                    new(1, 1, 20, 1, 3),
                                                    new(1, 1, 4, 90, 0),
                                                    new(1, 1, 4, 90, 4),
                                                    new(1, 1, 1, 0, 2),
                                                    new(1, 1, 3, 0, 3) };

            // HTTP 요청·응답과 연결 상태 순회
            foreach (var summary in invalid)
            {
                await RejectCharacterUnchanged(options, server, validNext with { Summary = summary! },
                                               HttpStatusCode.BadRequest,
                                               "summary shape and UE progress bounds rejected", owners);
            }

            // 아이템·장비 옵션·인벤토리와 창고 순회
            foreach (var field in new[] { "version", "heroClass", "level", "experience", "classTier" })
            {
                var zeroFieldRequest =
                    validNext with { Inventory = inventory with { HeroClass = 0, EquippedWeaponInstanceId =
                                                                                     inventory.Items[0].InstanceId },
                                     Summary = new(1, 0, 4, 0, 3) };
                var missing = JsonSerializer.SerializeToNode(zeroFieldRequest, Json)!.AsObject();
                missing["summary"]!.AsObject().Remove(field);
                await RejectCharacterUnchanged(options, server, missing, HttpStatusCode.BadRequest,
                                               "required summary field cannot default silently", owners);
            }

            // JSON 계약과 검증 기록에 필요한 값 준비
            var missingSummary = JsonSerializer.SerializeToNode(validNext, Json)!.AsObject();
            missingSummary.Remove("summary");
            await RejectCharacterUnchanged(options, server, missingSummary, HttpStatusCode.BadRequest,
                                           "summary itself required", owners);
            var extra = JsonSerializer.SerializeToNode(validNext, Json)!.AsObject();
            extra["summary"]!.AsObject()["gold"] = 9000;
            await RejectCharacterUnchanged(options, server, extra, HttpStatusCode.BadRequest,
                                           "undefined summary fields rejected", owners);
            await RejectCharacterUnchanged(
                options, server, legacy with { OperationId = Guid.NewGuid(), ExpectedRevision = 3 },
                HttpStatusCode.Conflict, "inventory-only new mutation blocked after summary exists", owners,
                InventoryCommitRoute);
            await RejectCharacterUnchanged(
                options, server, opaque with { OperationId = Guid.NewGuid(), ExpectedRevision = 3 },
                HttpStatusCode.Conflict, "old opaque new mutation still blocked", owners, "/v1/server/commit");
            var historicalInventory = await Call(server, InventoryCommitRoute, legacy, HttpStatusCode.OK);
            var historicalOpaque = await Call(server, "/v1/server/commit", opaque, HttpStatusCode.OK);
            Check(historicalInventory.GetProperty("revision").GetInt64() == 2 &&
                      historicalOpaque.GetProperty("revision").GetInt64() == 1 &&
                      await CharacterFingerprint(options, hero.PlayerId) == afterFirst,
                  "both old receipt domains remain read-only after summary backfill");
            await RejectCharacterUnchanged(options, server, validNext with { OperationId = legacy.OperationId },
                                           HttpStatusCode.Conflict,
                                           "new request hash domain cannot collide with old receipt", owners);

            // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
            var failure =
                validNext with { Inventory = ReplaceItem(inventory, 5, i => i with { Upgrade = 1, Durability = 119 }),
                                 Wallet = wallet with { Gold = 460 },
                                 Summary = first.Summary with { Level = 5, Experience = 0 } };
            await Sql(
                options,
                $"ALTER TABLE character_journal ADD CONSTRAINT character_atomic_fault CHECK(player_id <> '{hero.PlayerId:D}'::uuid OR revision <> 4)");

            // SQL 명령과 조회 결과 처리
            try
            {
                await RejectCharacterUnchanged(options, server, failure, HttpStatusCode.ServiceUnavailable,
                                               "last summary journal failure rolls back items/blob/wallet/all journals",
                                               owners);
            }
            finally
            {
                await Sql(options, "ALTER TABLE character_journal DROP CONSTRAINT character_atomic_fault");
            }

            // 접속 권한·저장 리비전·멱등 키 처리
            await AcceptCharacter(options, server, user, "character_hero", failure);
            await AssertSummaryJournal(options, failure, first.Summary);
            var raceA =
                failure with { OperationId = Guid.NewGuid(), ExpectedRevision = 4, Profile = Blob("summary-race-a"),
                               Summary = new(1, 1, 20, 0, 3), Wallet = failure.Wallet with { Gold = 461 } };
            var raceB =
                failure with { OperationId = Guid.NewGuid(), ExpectedRevision = 4, Profile = Blob("summary-race-b"),
                               Summary = new(1, 1, 19, 1899, 3), Wallet = failure.Wallet with { Gold = 462 } };
            var race = await Task.WhenAll(server.PostAsJsonAsync(CharacterRoute, raceA),
                                          server.PostAsJsonAsync(CharacterRoute, raceB));
            CommitCharacterRequest winner;

            // HTTP 요청·응답과 연결 상태 처리
            try
            {
                Check(race.Count(r => r.StatusCode == HttpStatusCode.OK) == 1 &&
                          race.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1,
                      "character CAS race has one complete winner and one loser");
                winner = race[0].StatusCode == HttpStatusCode.OK ? raceA : raceB;
            }
            finally
            {
                foreach (var response in race)
                {
                    response.Dispose();
                }
            }

            await AssertCharacter(options, user, "character_hero", winner);
            await AssertSummaryJournal(options, winner, failure.Summary);
            await AssertCharacter(options, other, "character_peer", peerFirst);

            // Revoke one token and its unclaimed tickets, preserve another session and active game lease.

            // 아이템·장비 옵션·인벤토리와 창고에 필요한 값 준비
            const string password = "Inventory-Integration-Only-123";
            var secondSession =
                await Call(anonymous, "/v1/sessions", new { name = "character_hero", password }, HttpStatusCode.OK);
            using var second = Client(address, token: secondSession.GetProperty("accessToken").GetString());
            var ticketA =
                (await Call(user, "/v1/join-tickets", new {}, HttpStatusCode.OK)).GetProperty("ticket").GetString();
            var ticketB =
                (await Call(second, "/v1/join-tickets", new {}, HttpStatusCode.OK)).GetProperty("ticket").GetString();
            await Call(user, "/v1/sessions/logout", new {}, HttpStatusCode.OK);
            await Call(user, "/v1/me/character", null, HttpStatusCode.Unauthorized);
            await Call(user, "/v1/join-tickets", new {}, HttpStatusCode.Unauthorized);
            await Call(user, "/v1/sessions/logout", new {}, HttpStatusCode.Unauthorized);
            await Call(server, "/v1/server/claim", new { ticket = ticketA, serverId = "revoked-ticket" },
                       HttpStatusCode.Unauthorized);
            var active = await Call(second, "/v1/me", null, HttpStatusCode.OK);
            Check(active.GetProperty("leaseActive").GetBoolean(),
                  "session logout preserves active authoritative game lease for final save");
            await AssertCharacter(options, second, "character_hero", winner);
            var final = winner with { ExpectedRevision = 5, OperationId = Guid.NewGuid(),
                                      Profile = Blob("post-logout-final-save") };
            await AcceptCharacter(options, server, second, "character_hero", final);
            await Call(other, "/v1/server/release-confirmed", new LeaseRequest(hero.PlayerId, hero.LeaseId),
                       HttpStatusCode.Unauthorized);
            await Call(server, "/v1/server/release-confirmed", new LeaseRequest(hero.PlayerId, peer.LeaseId),
                       HttpStatusCode.Conflict);
            await Call(server, "/v1/server/release-confirmed", new LeaseRequest(hero.PlayerId, hero.LeaseId),
                       HttpStatusCode.OK);
            await Call(server, "/v1/server/release-confirmed", new LeaseRequest(hero.PlayerId, hero.LeaseId),
                       HttpStatusCode.OK);
            Check(Convert.ToInt64(await Sql(options, "SELECT count(*) FROM released_leases WHERE player_id=$1",
                                            hero.PlayerId)) == 1,
                  "confirmed release retry produces one receipt");
            var restored =
                await Call(server, "/v1/server/claim", new { ticket = ticketB, serverId = "second-session-reconnect" },
                           HttpStatusCode.OK);
            var newLease = restored.GetProperty("leaseId").GetGuid();
            Check(restored.GetProperty("summary").Deserialize<CharacterSummary>(Json) == final.Summary &&
                      restored.GetProperty("profileHash").GetString() == BlobHash(final.Profile) &&
                      restored.GetProperty("profile").GetString() == final.Profile &&
                      restored.GetProperty("wallet").Deserialize<Wallet>(Json) == final.Wallet &&
                      SameJson(restored.GetProperty("inventory").Deserialize<InventorySnapshot>(Json), final.Inventory),
                  "new claim restores exact summary and opaque/canonical representations together");
            var beforeReleaseReplay = await CharacterFingerprint(options, hero.PlayerId);
            await Call(server, "/v1/server/release-confirmed", new LeaseRequest(hero.PlayerId, hero.LeaseId),
                       HttpStatusCode.OK);
            Check(beforeReleaseReplay == await CharacterFingerprint(options, hero.PlayerId) &&
                      (Guid?)await Sql(options, "SELECT lease_id FROM profiles WHERE player_id=$1", hero.PlayerId) ==
                          newLease,
                  "old confirmed release receipt never releases a newer writer");
            var oldAck = await Call(server, CharacterRoute, final, HttpStatusCode.OK);
            Check(oldAck.GetProperty("revision").GetInt64() == 6 &&
                      await CharacterFingerprint(options, hero.PlayerId) == beforeReleaseReplay,
                  "character receipt after lease replacement remains read-only");
            await RejectCharacterUnchanged(
                options, server, final with { OperationId = Guid.NewGuid(), ExpectedRevision = 6 },
                HttpStatusCode.Conflict, "old character lease cannot mutate after reconnect", owners);
            await Call(server, "/v1/server/release", new LeaseRequest(hero.PlayerId, hero.LeaseId),
                       HttpStatusCode.Conflict);
            await Call(server, "/v1/server/release-confirmed", new LeaseRequest(hero.PlayerId, newLease),
                       HttpStatusCode.OK);

            // 접속 권한·저장 리비전·멱등 키 처리
            await Sql(options,
                      "UPDATE profiles SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE player_id=$1",
                      peer.PlayerId);
            await Call(server, "/v1/server/release-confirmed", new LeaseRequest(peer.PlayerId, peer.LeaseId),
                       HttpStatusCode.Conflict);
            var peerTicket =
                (await Call(other, "/v1/join-tickets", new {}, HttpStatusCode.OK)).GetProperty("ticket").GetString();
            var peerClaim = await Call(server, "/v1/server/claim",
                                       new { ticket = peerTicket, serverId = "peer-reconnect" }, HttpStatusCode.OK);
            await Call(server, "/v1/server/release-confirmed",
                       new LeaseRequest(peer.PlayerId, peerClaim.GetProperty("leaseId").GetGuid()), HttpStatusCode.OK);
            var thirdSession =
                await Call(anonymous, "/v1/sessions", new { name = "character_hero", password }, HttpStatusCode.OK);
            using var racingUser = Client(address, token: thirdSession.GetProperty("accessToken").GetString());
            var concurrent = await Task.WhenAll(racingUser.PostAsJsonAsync("/v1/join-tickets", new {}),
                                                racingUser.PostAsJsonAsync("/v1/sessions/logout", new {}));

            // JSON 계약과 검증 기록 처리
            try
            {
                Check(concurrent[1].StatusCode == HttpStatusCode.OK &&
                          concurrent[0].StatusCode is HttpStatusCode.OK or HttpStatusCode.Unauthorized,
                      "logout and ticket issuance serialize without a server error");
                if (concurrent[0].StatusCode == HttpStatusCode.OK)
                {
                    var issued = await concurrent[0].Content.ReadFromJsonAsync<JsonElement>();
                    await Call(server, "/v1/server/claim",
                               new { ticket = issued.GetProperty("ticket").GetString(), serverId = "logout-race" },
                               HttpStatusCode.Unauthorized);
                }
                else
                {
                    Check(true, "logout won before any ticket was issued");
                }
            }
            finally
            {
                foreach (var response in concurrent)
                {
                    response.Dispose();
                }
            }

            // SQL 명령과 조회 결과 처리
            await Call(racingUser, "/v1/servers", null, HttpStatusCode.Unauthorized);
            await Call(second, "/v1/servers", null, HttpStatusCode.OK);
            var heroCheckpoint = await CaptureCharacterCheckpoint(options, final);
            var peerCheckpoint = await CaptureCharacterCheckpoint(options, peerFirst);
            Check(heroCheckpoint.CharacterJournalRows == 4 && peerCheckpoint.CharacterJournalRows == 1,
                  "only successful character commits create exactly one summary journal");
            checkpoint = new(1, options.Schema, [heroCheckpoint, peerCheckpoint]);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }

        // 서버 시각과 재사용 대기시간 처리
        await VerifyCharacterPersistence(
            options, checkpoint ?? throw new InvalidOperationException("Character suite checkpoint missing."));
        await WriteNew(Path.Combine(output, "character-persistence-checkpoint.json"), checkpoint);
        await WriteNew(Path.Combine(output, "character-integration.json"), new {
            status = "passed", checkedUtc = DateTime.UtcNow, options.Schema, checks = Checks.Skip(firstCheck).ToArray(),
            migration, databaseRestartObserved = false,
            scope =
                "Real HTTP/SQL in unique test schemas; opaque profile bytes are synthetic API contract fixtures. No real UE data conversion or network game success is inferred."
        });
    }
}
