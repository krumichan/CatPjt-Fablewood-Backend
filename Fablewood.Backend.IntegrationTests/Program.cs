using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fablewood.Backend;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

internal static partial class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly List<string> Checks = [];
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Checks.Add(label); Console.WriteLine("PASS " + label);
    }
    private static async Task<JsonElement> Call(HttpClient client, string path, object? body, HttpStatusCode expected)
    {
        using var response = body is null ? await client.GetAsync(path) : await client.PostAsJsonAsync(path, body);
        Check(response.StatusCode == expected, path + " HTTP " + (int)expected);
        var content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? default : JsonSerializer.Deserialize<JsonElement>(content, Json);
    }
    private static HttpClient Client(string address, string? serverKey = null, string? token = null)
    {
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(20) };
        if (serverKey is not null) client.DefaultRequestHeaders.Add("X-Fable-Server-Key", serverKey);
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private static async Task<object?> Sql(BackendOptions options, string sql, params object[] values)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString); await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.Add(new NpgsqlParameter { Value = value });
        return await command.ExecuteScalarAsync();
    }
    private static BackendOptions Isolated(BackendOptions configured, string schema)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(schema, "^fable_test_[a-f0-9]{32}$")) throw new InvalidOperationException("Refusing a non-test schema.");
        var connection = new NpgsqlConnectionStringBuilder(configured.ConnectionString) { SearchPath = schema };
        return configured with { ConnectionString = connection.ConnectionString, Schema = schema };
    }
    private static async Task WriteNew(string path, object value)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await file.WriteAsync(data); await file.FlushAsync();
    }
    private sealed record Checkpoint(string Schema, Guid PlayerId, long Revision, string ProfileHash, Wallet Wallet, long Operations, long LedgerRows);
    private static async Task Verify(BackendOptions options, Checkpoint expected)
    {
        await using var connection = new NpgsqlConnection(options.ConnectionString); await connection.OpenAsync();
        await using var query = new NpgsqlCommand("SELECT revision,profile,gold,ore,essence,root_cores,moon_crystals,health_potions,mana_potions,(SELECT count(*) FROM operations WHERE player_id=$1),(SELECT count(*) FROM economy_ledger WHERE player_id=$1) FROM profiles WHERE player_id=$1", connection);
        query.Parameters.AddWithValue(expected.PlayerId);
        await using var reader = await query.ExecuteReaderAsync();
        Check(await reader.ReadAsync(), "persistent player exists");
        Check(reader.GetInt64(0) == expected.Revision, "persistent revision exact");
        Check(Convert.ToHexStringLower(SHA256.HashData(reader.GetFieldValue<byte[]>(1))) == expected.ProfileHash, "persistent raw profile SHA exact");
        var wallet = new Wallet(reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8));
        Check(wallet == expected.Wallet, "persistent canonical wallet exact");
        Check(reader.GetInt64(9) == expected.Operations && reader.GetInt64(10) == expected.LedgerRows, "persistent operation and ledger counts exact");
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var configured = BackendOptions.FromEnvironment();
            if (args.Length == 2 && args[0] == "--verify-persistence")
            {
                var path = Path.GetFullPath(args[1]);
                var expected = JsonSerializer.Deserialize<Checkpoint>(await File.ReadAllTextAsync(path), Json) ?? throw new InvalidOperationException("Missing checkpoint.");
                await Verify(Isolated(configured, expected.Schema), expected);
                var inventoryCheckpointPath = Path.Combine(Path.GetDirectoryName(path)!, "inventory-persistence-checkpoint.json");
                if (File.Exists(inventoryCheckpointPath))
                {
                    var inventoryCheckpoint = JsonSerializer.Deserialize<InventoryCheckpoint>(await File.ReadAllTextAsync(inventoryCheckpointPath), Json)
                        ?? throw new InvalidOperationException("Missing inventory checkpoint.");
                    if (inventoryCheckpoint.Schema != expected.Schema) throw new InvalidOperationException("Inventory checkpoint belongs to a different isolated schema.");
                    await VerifyInventoryPersistence(Isolated(configured, expected.Schema), inventoryCheckpoint);
                }
                var characterCheckpointPath = Path.Combine(Path.GetDirectoryName(path)!, "character-persistence-checkpoint.json");
                if (File.Exists(characterCheckpointPath))
                {
                    var characterCheckpoint = JsonSerializer.Deserialize<CharacterCheckpoint>(await File.ReadAllTextAsync(characterCheckpointPath), Json)
                        ?? throw new InvalidOperationException("Missing character checkpoint.");
                    if (characterCheckpoint.Schema != expected.Schema) throw new InvalidOperationException("Character checkpoint schema mismatch.");
                    await VerifyCharacterPersistence(Isolated(configured, expected.Schema), characterCheckpoint);
                }
                await WriteNew(Path.Combine(Path.GetDirectoryName(path)!, "persistence-after-reconnect.json"), new { status = "passed", checkedUtc = DateTime.UtcNow, expected.Schema, checks = Checks, scope = "New connection after an operator-controlled restart; this program does not restart PostgreSQL or assert that a restart occurred." });
                return 0;
            }
            if (args.Length != 2 || args[0] != "--output") throw new InvalidOperationException("Usage: --output <new-evidence-directory> OR --verify-persistence <checkpoint.json>");
            var output = Path.GetFullPath(args[1]);
            if (Directory.Exists(output)) throw new InvalidOperationException("Existing evidence directory is preserved.");
            Directory.CreateDirectory(output);
            var options = Isolated(configured, "fable_test_" + Guid.NewGuid().ToString("N"));
            await using (var store = new SqlStore(options)) { await store.Migrate(CancellationToken.None); await store.Migrate(CancellationToken.None); }
            var app = BackendApplication.Build(options); app.Urls.Clear(); app.Urls.Add("http://127.0.0.1:0"); await app.StartAsync();
            Checkpoint? checkpoint = null;
            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
                using var anonymous = Client(address); using var server = Client(address, options.ServerKey); using var wrongKey = Client(address, "not-a-server-key");
                await Call(anonymous, "/health", null, HttpStatusCode.OK);
                await Call(anonymous, "/v1/server/claim", new { ticket = Rules.Token(), serverId = "test" }, HttpStatusCode.Unauthorized);
                await Call(wrongKey, "/v1/server/claim", new { ticket = Rules.Token(), serverId = "test" }, HttpStatusCode.Unauthorized);
                const string name = "integration_hero"; const string password = "Integration-Only-Password-123";
                await Call(anonymous, "/v1/accounts", new { name, password, gold = 1000 }, HttpStatusCode.BadRequest);
                var account = await Call(anonymous, "/v1/accounts", new { name, password, initialClass = 1 }, HttpStatusCode.Created);
                var player = account.GetProperty("playerId").GetGuid();
                await Call(anonymous, "/v1/accounts", new { name = name.ToUpperInvariant(), password }, HttpStatusCode.Conflict);
                await Call(anonymous, "/v1/sessions", new { name, password = "wrong-password-123" }, HttpStatusCode.Unauthorized);
                var session = await Call(anonymous, "/v1/sessions", new { name, password }, HttpStatusCode.OK);
                var access = session.GetProperty("accessToken").GetString()!;
                using var user = Client(address, token: access);
                var me = await Call(user, "/v1/me", null, HttpStatusCode.OK);
                Check(me.GetProperty("wallet").Deserialize<Wallet>(Json) == Wallet.Initial && me.GetProperty("profileHash").ValueKind == JsonValueKind.Null, "new profile canonical initial wallet");
                var issuedTicket = await Call(user, "/v1/join-tickets", new { }, HttpStatusCode.OK);
                var ticket = issuedTicket.GetProperty("ticket").GetString()!;
                var ticketExpiry = issuedTicket.GetProperty("expiresAt").GetDateTime();
                var storedTicketExpiry = (DateTime)(await Sql(options, "SELECT expires_at FROM join_tickets WHERE token_hash=$1", Rules.TokenHash(ticket)))!;
                var ticketLifetime = Convert.ToDouble(await Sql(options, "SELECT extract(epoch FROM expires_at-created_at) FROM join_tickets WHERE token_hash=$1", Rules.TokenHash(ticket)));
                Check(ticketExpiry == storedTicketExpiry, "join ticket expiry response matches stored DB timestamp");
                // Both times originate in one DB INSERT; allow sub-second evaluation skew, never the old 60-second lifetime.
                Check(Math.Abs(ticketLifetime - 180.0) < 1.0, "join ticket lifetime is 180 seconds on DB clock");
                var claim = await Call(server, "/v1/server/claim", new { ticket, serverId = "integration-server" }, HttpStatusCode.OK);
                var lease = claim.GetProperty("leaseId").GetGuid(); var initialExpiry = claim.GetProperty("leaseExpiry").GetDateTime();
                Check(claim.GetProperty("initialClass").GetInt32() == 1 && claim.GetProperty("profile").ValueKind == JsonValueKind.Null && claim.GetProperty("revision").GetInt64() == 0, "new claim class/null blob/revision");
                await Call(server, "/v1/server/claim", new { ticket, serverId = "integration-server" }, HttpStatusCode.Conflict);
                var secondTicket = (await Call(user, "/v1/join-tickets", new { }, HttpStatusCode.OK)).GetProperty("ticket").GetString()!;
                await Call(server, "/v1/server/claim", new { ticket = secondTicket, serverId = "other-server" }, HttpStatusCode.Conflict);
                var blob = Convert.ToBase64String(Encoding.UTF8.GetBytes("opaque-test-profile-v8-initial"));
                var first = new CommitRequest(player, lease, Guid.NewGuid(), 0, blob, Wallet.Initial with { Gold = 10 });
                await Call(user, "/v1/server/commit", first, HttpStatusCode.Unauthorized);
                var committed = await Call(server, "/v1/server/commit", first, HttpStatusCode.OK);
                var sha = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(blob)));
                Check(committed.GetProperty("revision").GetInt64() == 1 && committed.GetProperty("profileHash").GetString() == sha, "commit ACK revision/raw hash");
                var replay = await Call(server, "/v1/server/commit", first, HttpStatusCode.OK);
                Check(replay.GetProperty("revision").GetInt64() == 1 && replay.GetProperty("profileHash").GetString() == sha, "same operation exact idempotent ACK");
                await Call(server, "/v1/server/commit", first with { Wallet = first.Wallet with { Gold = 9 } }, HttpStatusCode.Conflict);
                await Call(server, "/v1/server/commit", first with { OperationId = Guid.NewGuid(), ExpectedRevision = 1, Wallet = first.Wallet with { Gold = -1 } }, HttpStatusCode.BadRequest);
                await Call(server, "/v1/server/commit", first with { OperationId = Guid.NewGuid(), ExpectedRevision = 1, Wallet = first.Wallet with { HealthPotions = 21 } }, HttpStatusCode.BadRequest);
                await Call(server, "/v1/server/commit", first with { OperationId = Guid.NewGuid(), ExpectedRevision = 1, Profile = blob + " " }, HttpStatusCode.BadRequest);
                await Call(server, "/v1/server/commit", first with { OperationId = Guid.NewGuid(), ExpectedRevision = 1, Profile = Convert.ToBase64String(new byte[Rules.MaxProfileBytes + 1]) }, HttpStatusCode.BadRequest);
                var raceA = first with { OperationId = Guid.NewGuid(), ExpectedRevision = 1, Wallet = first.Wallet with { Gold = 20 } };
                var raceB = raceA with { OperationId = Guid.NewGuid(), Wallet = raceA.Wallet with { Gold = 30 } };
                using var responseA = await server.PostAsJsonAsync("/v1/server/commit", raceA);
                // A second parallel race is issued below from the now-current revision.
                Check(responseA.StatusCode == HttpStatusCode.OK, "serial baseline revision two");
                raceA = raceA with { OperationId = Guid.NewGuid(), ExpectedRevision = 2, Wallet = raceA.Wallet with { Gold = 40 } };
                raceB = raceB with { OperationId = Guid.NewGuid(), ExpectedRevision = 2, Wallet = raceB.Wallet with { Gold = 50 } };
                var responses = await Task.WhenAll(server.PostAsJsonAsync("/v1/server/commit", raceA), server.PostAsJsonAsync("/v1/server/commit", raceB));
                Check(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1 && responses.Count(r => r.StatusCode == HttpStatusCode.Conflict) == 1, "concurrent expected revision has exactly one winner");
                foreach (var response in responses) response.Dispose();
                me = await Call(user, "/v1/me", null, HttpStatusCode.OK);
                var wallet = me.GetProperty("wallet").Deserialize<Wallet>(Json)!;
                Check(me.GetProperty("revision").GetInt64() == 3 && wallet.Gold is 40 or 50, "race canonical wallet belongs to winner");
                // Fault injection belongs solely to this unique integration schema.
                await Sql(options, "ALTER TABLE economy_ledger ADD CONSTRAINT integration_atomic_fault CHECK(gold_delta<>13)");
                await Call(server, "/v1/server/commit", first with { OperationId = Guid.NewGuid(), ExpectedRevision = 3, Wallet = wallet with { Gold = wallet.Gold + 13 } }, HttpStatusCode.ServiceUnavailable);
                await Sql(options, "ALTER TABLE economy_ledger DROP CONSTRAINT integration_atomic_fault");
                var afterFault = await Call(user, "/v1/me", null, HttpStatusCode.OK);
                Check(afterFault.GetProperty("revision").GetInt64() == 3 && afterFault.GetProperty("wallet").Deserialize<Wallet>(Json) == wallet, "ledger failure rolls profile and wallet back atomically");
                Check(Convert.ToInt64(await Sql(options, "SELECT count(*) FROM operations WHERE player_id=$1", player)) == 3 && Convert.ToInt64(await Sql(options, "SELECT count(*) FROM economy_ledger WHERE player_id=$1", player)) == 3, "failed/replayed commits add no operation or ledger rows");
                Check(Convert.ToInt64(await Sql(options, "SELECT sum(gold_delta) FROM economy_ledger WHERE player_id=$1", player)) == wallet.Gold, "ledger gold deltas reconcile to wallet");
                var heartbeat = await Call(server, "/v1/server/heartbeat", new LeaseRequest(player, lease), HttpStatusCode.OK);
                Check(heartbeat.GetProperty("leaseExpiry").GetDateTime() > initialExpiry, "heartbeat extends DB-clock lease");
                await Sql(options, "UPDATE profiles SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE player_id=$1", player);
                await Call(server, "/v1/server/heartbeat", new LeaseRequest(player, lease), HttpStatusCode.Conflict);
                await Call(server, "/v1/server/commit", first with { OperationId = Guid.NewGuid(), ExpectedRevision = 3, Wallet = wallet }, HttpStatusCode.Conflict);
                var reclaimed = await Call(server, "/v1/server/claim", new { ticket = secondTicket, serverId = "replacement-server" }, HttpStatusCode.OK);
                var newLease = reclaimed.GetProperty("leaseId").GetGuid();
                Check(newLease != lease && reclaimed.GetProperty("revision").GetInt64() == 3 && reclaimed.GetProperty("profileHash").GetString() == sha && reclaimed.GetProperty("profile").GetString() == blob, "new lease fences stale writer and restores exact blob");
                await Call(server, "/v1/server/commit", first with { OperationId = Guid.NewGuid(), ExpectedRevision = 3, Wallet = wallet }, HttpStatusCode.Conflict);
                await Call(server, "/v1/server/release", new LeaseRequest(player, lease), HttpStatusCode.Conflict);
                await Call(server, "/v1/server/release", new LeaseRequest(player, newLease), HttpStatusCode.OK);
                var releasedMe = await Call(user, "/v1/me", null, HttpStatusCode.OK);
                Check(releasedMe.GetProperty("leaseActive").GetBoolean() == false, "public self status observes released lease without exposing ID");
                var expiredTicket = (await Call(user, "/v1/join-tickets", new { }, HttpStatusCode.OK)).GetProperty("ticket").GetString()!;
                await Sql(options, "UPDATE join_tickets SET expires_at=clock_timestamp()-interval '1 second' WHERE token_hash=$1", Rules.TokenHash(expiredTicket));
                await Call(server, "/v1/server/claim", new { ticket = expiredTicket, serverId = "integration-server" }, HttpStatusCode.Unauthorized);
                await Sql(options, "UPDATE sessions SET expires_at=clock_timestamp()-interval '1 second' WHERE token_hash=$1", Rules.TokenHash(access));
                await Call(user, "/v1/me", null, HttpStatusCode.Unauthorized);
                await Call(anonymous, "/v1/accounts", new { name = "second_hero", password }, HttpStatusCode.Created);
                var otherSession = await Call(anonymous, "/v1/sessions", new { name = "second_hero", password }, HttpStatusCode.OK);
                using var other = Client(address, token: otherSession.GetProperty("accessToken").GetString());
                var otherMe = await Call(other, "/v1/me", null, HttpStatusCode.OK);
                Check(otherMe.GetProperty("playerId").GetGuid() != player && otherMe.GetProperty("wallet").Deserialize<Wallet>(Json) == Wallet.Initial, "client reads only its own canonical wallet");
                checkpoint = new(options.Schema, player, 3, sha, wallet, 3, 3);
                await Verify(options, checkpoint);
            }
            finally { await app.StopAsync(); await app.DisposeAsync(); }
            await Verify(options, checkpoint!); // New connections after the API datasource is closed.
            if (Checks.Count != 65) throw new InvalidOperationException("The unchanged legacy regression must complete all 65 checks before inventory coverage starts.");
            var legacyCheckCount = Checks.Count;
            await WriteNew(Path.Combine(output, "persistence-checkpoint.json"), checkpoint!);
            await RunInventorySuite(options, output);
            var inventoryCheckCount = Checks.Count - legacyCheckCount;
            if (inventoryCheckCount != 251) throw new InvalidOperationException("The preserved inventory regression must complete 251 checks.");
            await RunCharacterSuite(options, output);
            await WriteNew(Path.Combine(output, "integration.json"), new { status = "passed", checkedUtc = DateTime.UtcNow, options.Schema, legacyCheckCount, inventoryCheckCount, characterCheckCount = Checks.Count - legacyCheckCount - inventoryCheckCount, checks = Checks, databaseRestartObserved = false, scope = "Unique PostgreSQL schema, real loopback HTTP. Expiry/fault fixtures only in this schema. PostgreSQL restart is an explicit operator follow-up; no real game profile was mutated." });
            Console.WriteLine("Integration checks passed. Preserve the isolated schema for explicit restart verification."); return 0;
        }
        catch (PostgresException error)
        {
            // Never print Message/Detail, SQL text, connection strings, credentials or profile bytes.
            Console.Error.WriteLine($"Integration PostgreSQL failed: checks={Checks.Count} sqlstate={error.SqlState} severity={error.InvariantSeverity} schema={error.SchemaName ?? "-"} table={error.TableName ?? "-"} column={error.ColumnName ?? "-"} constraint={error.ConstraintName ?? "-"} routine={error.Routine ?? "-"} position={error.Position}");
            return 1;
        }
        catch (Exception error) { Console.Error.WriteLine("Integration failed: " + error.GetType().Name + (error is InvalidOperationException ? " / " + error.Message : "")); return 1; }
    }
}
