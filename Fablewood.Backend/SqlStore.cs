using System.Security.Cryptography;
using Npgsql;

namespace Fablewood.Backend;

/// <summary>계정·접속 권한·프로필·장비를 트랜잭션으로 보존하는 PostgreSQL 저장소.</summary>
public sealed partial class SqlStore(BackendOptions options) : IAsyncDisposable
{
    private readonly NpgsqlDataSource data = NpgsqlDataSource.Create(options.ConnectionString);
    private static readonly byte[] DummySalt = new byte[32];
    private static readonly byte[] DummyHash = new byte[32];
    private const string WalletColumns = "gold,ore,essence,root_cores,moon_crystals,health_potions,mana_potions";

    /// <summary>연결·트랜잭션·위치 매개변수를 갖는 SQL 명령을 만든다.</summary>
    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
                                         params object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }
        return command;
    }

    /// <summary>격리된 스키마에 필요한 데이터베이스 변경을 순서대로 적용한다.</summary>
    public async Task Migrate(CancellationToken ct)
    {
        // 데이터베이스 트랜잭션에 필요한 값 준비
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var gate = Command(connection, transaction, "SELECT pg_advisory_xact_lock(hashtextextended($1,0))",
                                        options.Schema))
        {
            await gate.ExecuteNonQueryAsync(ct);
        }
        // Schema identifier was restricted to ASCII alphanumeric/underscore in options.
        await using (var schema = Command(connection, transaction, $"CREATE SCHEMA IF NOT EXISTS \"{options.Schema}\""))
        {
            await schema.ExecuteNonQueryAsync(ct);
        }
        await using (
            var bootstrap = Command(
                connection, transaction,
                "CREATE TABLE IF NOT EXISTS schema_migrations(version integer PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT clock_timestamp())"))
        {
            await bootstrap.ExecuteNonQueryAsync(ct);
        }
        int current;
        await using (var version =
                         Command(connection, transaction, "SELECT COALESCE(max(version),0) FROM schema_migrations"))
        {
            current = Convert.ToInt32(await version.ExecuteScalarAsync(ct));
        }

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (current is < 0 or > 3)
        {
            throw new InvalidOperationException("Unsupported database schema version.");
        }

        // SQL 명령과 조회 결과 순회
        for (int next = current + 1; next <= 3; next++)
        {
            // SQL 명령과 조회 결과에 필요한 값 준비
            string name = next switch {
                1 => "001_initial.sql",
                2 => "002_inventory.sql",
                _ => "003_character.sql"
            };
            await using var stream =
                typeof(SqlStore).Assembly.GetManifestResourceStream("Fablewood.Backend.Migrations." + name) ??
                throw new InvalidOperationException("Embedded migration missing.");
            using var reader = new StreamReader(stream);
            await using var migration = Command(connection, transaction, await reader.ReadToEndAsync(ct));
            await migration.ExecuteNonQueryAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    /// <summary>저장소 연결과 스키마 준비 상태를 검사한다.</summary>
    public async Task Health(CancellationToken ct)
    {
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var command = Command(connection, null, "SELECT max(version) FROM schema_migrations");
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 3)
        {
            throw new ApiError(503, "database_unavailable", "데이터베이스 준비가 필요합니다.");
        }
    }

    /// <summary>계정 입력을 검증하고 암호 해시와 초기 프로필을 저장한다.</summary>
    public async Task<Guid> CreateAccount(AccountRequest request, CancellationToken ct)
    {
        var name = Rules.Name(request.Name);
        Rules.Password(request.Password);

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (request.InitialClass is < 0 or > 2)
        {
            throw new ApiError(400, "invalid_class", "직업이 올바르지 않습니다.");
        }

        // 데이터베이스 트랜잭션에 필요한 값 준비
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rules.PasswordHash(request.Password, salt);
        var id = Guid.NewGuid();
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);

        // 데이터베이스 트랜잭션 처리
        try
        {
            await using (
                var insert = Command(
                    connection, transaction,
                    "INSERT INTO accounts(player_id,name,password_salt,password_hash,password_iterations,initial_class) VALUES($1,$2,$3,$4,$5,$6)",
                    id, name, salt, hash, Rules.PasswordIterations, request.InitialClass))
            {
                await insert.ExecuteNonQueryAsync(ct);
            }
            await using (var profile =
                             Command(connection, transaction, "INSERT INTO profiles(player_id) VALUES($1)", id))
            {
                await profile.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return id;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiError(409, "account_exists", "이미 사용 중인 계정 이름입니다.");
        }
    }

    /// <summary>계정 암호를 확인하고 만료 시간이 있는 로그인 세션을 발급한다.</summary>
    public async Task<object> CreateSession(SessionRequest request, CancellationToken ct)
    {
        var name = Rules.Name(request.Name);
        Rules.Password(request.Password);
        await using var connection = await data.OpenConnectionAsync(ct);
        Guid? id = null;
        byte[] salt = DummySalt, expected = DummyHash;
        await using (var query =
                         Command(connection, null,
                                 "SELECT player_id,password_salt,password_hash FROM accounts WHERE name=$1", name))
        {
            await using (var reader = await query.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    id = reader.GetGuid(0);
                    salt = reader.GetFieldValue<byte[]>(1);
                    expected = reader.GetFieldValue<byte[]>(2);
                }
            }
        }
        var actual = Rules.PasswordHash(request.Password, salt);
        var valid = CryptographicOperations.FixedTimeEquals(actual, expected);

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (!valid || id is null)
        {
            throw new ApiError(401, "invalid_credentials", "계정 이름 또는 비밀번호를 확인하세요.");
        }

        // SQL 명령과 조회 결과에 필요한 값 준비
        var token = Rules.Token();
        await using var insert = Command(
            connection, null,
            "INSERT INTO sessions(token_hash,player_id,expires_at) VALUES($1,$2,clock_timestamp()+interval '20 minutes') RETURNING expires_at",
            Rules.TokenHash(token), id.Value);
        var expiry = (DateTime)(await insert.ExecuteScalarAsync(ct))!;

        return new { accessToken = token, expiresAt = expiry };
    }

    /// <summary>토큰의 해시와 만료 시간을 확인해 플레이어를 인증한다.</summary>
    public async Task<Guid> Authenticate(string? token, CancellationToken ct)
    {
        // SQL 명령과 조회 결과에 필요한 값 준비
        var hash = Rules.TokenHash(token);
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var query =
            Command(connection, null,
                    "SELECT player_id FROM sessions WHERE token_hash=$1 AND expires_at>clock_timestamp()", hash);
        var result = await query.ExecuteScalarAsync(ct);

        return result is Guid id ? id : throw new ApiError(401, "session_expired", "로그인이 만료되었습니다.");
    }

    /// <summary>인증된 세션에 짧은 수명의 일회용 서버 접속 티켓을 발급한다.</summary>
    public async Task<object> IssueTicket(string? sessionToken, CancellationToken ct)
    {
        // 데이터베이스 트랜잭션에 필요한 값 준비
        var sessionHash = Rules.TokenHash(sessionToken);
        var ticket = Rules.Token();
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var player = await LockSession(connection, transaction, sessionHash, ct);
        // Lock order is session -> ticket -> profile, also used by claim/logout.
        await using var insert = Command(
            connection, transaction,
            "INSERT INTO join_tickets(token_hash,player_id,expires_at,session_token_hash) VALUES($1,$2,clock_timestamp()+interval '180 seconds',$3) RETURNING expires_at",
            Rules.TokenHash(ticket), player, sessionHash);
        var expiry = (DateTime)(await insert.ExecuteScalarAsync(ct))!;
        await transaction.CommitAsync(ct);

        return new { ticket, expiresAt = expiry };
    }

    /// <summary>트랜잭션이 잠근 프로필의 리비전·재화·접속 권한 조회 결과.</summary>
    private sealed record ProfileRow(long Revision, byte[]? Profile, Wallet Wallet, Guid? LeaseId, bool LeaseActive,
                                     int InitialClass, int InventoryVersion, int SummaryVersion);

    /// <summary>트랜잭션 안에서 프로필 행을 잠그고 현재 저장 상태를 읽는다.</summary>
    private static async Task<ProfileRow> LockedProfile(NpgsqlConnection connection, NpgsqlTransaction transaction,
                                                        Guid player, CancellationToken ct)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        await using var query = Command(
            connection, transaction,
            "SELECT p.revision,p.profile,p.gold,p.ore,p.essence,p.root_cores,p.moon_crystals,p.health_potions,p.mana_potions,p.lease_id,COALESCE(p.lease_expires_at>clock_timestamp(),false),a.initial_class,p.inventory_version,COALESCE(s.version,0) FROM profiles p JOIN accounts a ON a.player_id=p.player_id LEFT JOIN character_summaries s ON s.player_id=p.player_id WHERE p.player_id=$1 FOR UPDATE OF p",
            player);
        await using var reader = await query.ExecuteReaderAsync(ct);

        // SQL 명령과 조회 결과 조건 검사
        if (!await reader.ReadAsync(ct))
        {
            throw new ApiError(404, "player_missing", "플레이어를 찾을 수 없습니다.");
        }

        return new(reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<byte[]>(1),
                   ReadWallet(reader, 2), reader.IsDBNull(9) ? null : reader.GetGuid(9), reader.GetBoolean(10),
                   reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13));
    }

    /// <summary>SQL 조회 열에서 재화와 물약 수량을 읽는다.</summary>
    private static Wallet ReadWallet(NpgsqlDataReader reader, int start)
    {
        return new(reader.GetInt32(start), reader.GetInt32(start + 1), reader.GetInt32(start + 2),
                   reader.GetInt32(start + 3), reader.GetInt32(start + 4), reader.GetInt32(start + 5),
                   reader.GetInt32(start + 6));
    }

    /// <summary>접속 티켓을 한 번 소비하고 게임 서버의 프로필 접속 권한을 확보한다.</summary>
    public async Task<ClaimResponse> Claim(ClaimRequest request, CancellationToken ct)
    {
        // 데이터베이스 트랜잭션 처리
        Rules.Server(request.ServerId);
        var hash = Rules.TokenHash(request.Ticket);
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        Guid? sessionPlayer = null;
        await using (var session = Command(connection, transaction,
                                           "SELECT session_token_hash FROM join_tickets WHERE token_hash=$1", hash))
        {
            if (await session.ExecuteScalarAsync(ct) is byte[] sessionHash)
            {
                sessionPlayer = await LockSession(connection, transaction, sessionHash, ct);
            }
        }
        Guid player;
        await using (
            var ticket = Command(
                connection, transaction,
                "SELECT player_id,consumed_at,expires_at>clock_timestamp() FROM join_tickets WHERE token_hash=$1 FOR UPDATE",
                hash))
        {
            await using (var reader = await ticket.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct) || !reader.GetBoolean(2))
                {
                    throw new ApiError(401, "ticket_expired", "입장권이 없거나 만료되었습니다.");
                }
                if (!reader.IsDBNull(1))
                {
                    throw new ApiError(409, "ticket_used", "이미 사용한 입장권입니다.");
                }
                player = reader.GetGuid(0);
            }
        }

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (sessionPlayer is Guid sessionOwner && sessionOwner != player)
        {
            throw new ApiError(401, "ticket_expired", "입장권을 확인할 수 없습니다.");
        }

        var profile = await LockedProfile(connection, transaction, player, ct);

        // 접속 권한·저장 리비전·멱등 키 조건 검사
        if (profile.LeaseActive)
        {
            throw new ApiError(409, "already_online", "다른 접속이 아직 해당 캐릭터를 사용 중입니다.");
        }

        // 접속 권한·저장 리비전·멱등 키 처리
        await using (
            var consume = Command(
                connection, transaction,
                "UPDATE join_tickets SET consumed_at=clock_timestamp() WHERE token_hash=$1 AND consumed_at IS NULL AND expires_at>clock_timestamp()",
                hash))
        {
            if (await consume.ExecuteNonQueryAsync(ct) != 1)
            {
                throw new ApiError(401, "ticket_expired", "입장권이 만료되었습니다.");
            }
        }
        var lease = Guid.NewGuid();
        await using var update = Command(
            connection, transaction,
            "UPDATE profiles SET lease_id=$2,lease_server=$3,lease_expires_at=clock_timestamp()+interval '60 seconds' WHERE player_id=$1 RETURNING lease_expires_at",
            player, lease, request.ServerId);
        var expiry = (DateTime)(await update.ExecuteScalarAsync(ct))!;
        var inventory = await LoadInventory(connection, transaction, player, profile.InventoryVersion, ct);
        var summary = await LoadSummary(connection, transaction, player, ct);
        await transaction.CommitAsync(ct);

        return new(player, lease, expiry, profile.Revision, profile.InitialClass,
                   profile.Profile is null ? null : Convert.ToBase64String(profile.Profile),
                   profile.Profile is null ? null : Convert.ToHexStringLower(SHA256.HashData(profile.Profile)),
                   profile.Wallet, profile.InventoryVersion, inventory, summary);
    }

    /// <summary>접속 권한·리비전·멱등 키를 확인해 프로필과 재화를 원자적으로 저장한다.</summary>
    public async Task<CommitResponse> Commit(CommitRequest request, CancellationToken ct)
    {
        Rules.Identifiers(request.PlayerId, request.LeaseId, request.OperationId);

        // 접속 권한·저장 리비전·멱등 키 조건 검사
        if (request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue || request.Wallet is null)
        {
            throw new ApiError(400, "invalid_commit", "저장 요청이 올바르지 않습니다.");
        }

        // 데이터베이스 트랜잭션 처리
        request.Wallet.Validate();

        var blob = Rules.Profile(request.Profile);
        var hash = Rules.RequestHash(request);
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var profile = await LockedProfile(connection, transaction, request.PlayerId, ct);
        await using (var prior =
                         Command(connection, transaction,
                                 "SELECT request_hash,revision FROM operations WHERE player_id=$1 AND operation_id=$2",
                                 request.PlayerId, request.OperationId))
        {
            await using (var reader = await prior.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    if (!CryptographicOperations.FixedTimeEquals(hash, reader.GetFieldValue<byte[]>(0)))
                    {
                        throw new ApiError(409, "operation_conflict",
                                           "같은 작업 식별자에 다른 저장 내용이 전달되었습니다.");
                    }
                    return new(reader.GetInt64(1),
                               Convert.ToHexStringLower(SHA256.HashData(
                                   blob))); // Read-only replay is safe even after its old lease expires.
                }
            }
        }

        // 접속 권한·저장 리비전·멱등 키 조건 검사
        if (profile.LeaseId != request.LeaseId || !profile.LeaseActive)
        {
            throw new ApiError(409, "lease_lost", "저장 권한이 만료되었거나 다른 접속으로 바뀌었습니다.");
        }
        if (profile.Revision != request.ExpectedRevision)
        {
            throw new ApiError(409, "revision_conflict", "더 최신 저장이 존재합니다.");
        }
        if (profile.InventoryVersion != 0)
        {
            throw new ApiError(409, "inventory_required", "구조화 장비 저장이 필요한 프로필입니다.");
        }

        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        var w = request.Wallet;
        long revision;
        await using (
            var update = Command(
                connection, transaction,
                "UPDATE profiles SET revision=revision+1,profile=$4,gold=$5,ore=$6,essence=$7,root_cores=$8,moon_crystals=$9,health_potions=$10,mana_potions=$11,updated_at=clock_timestamp() WHERE player_id=$1 AND lease_id=$2 AND revision=$3 AND lease_expires_at>clock_timestamp() RETURNING revision",
                request.PlayerId, request.LeaseId, request.ExpectedRevision, blob, w.Gold, w.Ore, w.Essence,
                w.RootCores, w.MoonCrystals, w.HealthPotions, w.ManaPotions))
        {
            var result = await update.ExecuteScalarAsync(ct);
            if (result is not long value)
            {
                throw new ApiError(409, "lease_lost", "저장 권한이 만료되었습니다.");
            }
            revision = value;
        }
        await using (var operation = Command(
                         connection, transaction,
                         "INSERT INTO operations(player_id,operation_id,request_hash,revision) VALUES($1,$2,$3,$4)",
                         request.PlayerId, request.OperationId, hash, revision))
        {
            await operation.ExecuteNonQueryAsync(ct);
        }
        var old = profile.Wallet;
        await using (
            var ledger = Command(
                connection, transaction,
                "INSERT INTO economy_ledger(player_id,operation_id,revision,gold_delta,ore_delta,essence_delta,root_cores_delta,moon_crystals_delta,health_potions_delta,mana_potions_delta) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)",
                request.PlayerId, request.OperationId, revision, w.Gold - old.Gold, w.Ore - old.Ore,
                w.Essence - old.Essence, w.RootCores - old.RootCores, w.MoonCrystals - old.MoonCrystals,
                w.HealthPotions - old.HealthPotions, w.ManaPotions - old.ManaPotions))
        {
            await ledger.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);

        return new(revision, Convert.ToHexStringLower(SHA256.HashData(blob)));
    }

    /// <summary>유효한 게임 서버의 프로필 접속 권한 만료 시간을 연장한다.</summary>
    public async Task<object> Heartbeat(LeaseRequest request, CancellationToken ct)
    {
        // 접속 권한·저장 리비전·멱등 키 처리
        Rules.Identifiers(request.PlayerId, request.LeaseId);
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var update = Command(
            connection, null,
            "UPDATE profiles SET lease_expires_at=clock_timestamp()+interval '60 seconds' WHERE player_id=$1 AND lease_id=$2 AND lease_expires_at>clock_timestamp() RETURNING lease_expires_at",
            request.PlayerId, request.LeaseId);
        var result = await update.ExecuteScalarAsync(ct);

        return result is DateTime expiry ? new { leaseExpiry = expiry }
                                         : throw new ApiError(409, "lease_lost", "접속 권한이 만료되었습니다.");
    }

    /// <summary>유효한 접속 권한 또는 검증 입력의 유지 상태를 해제한다.</summary>
    public async Task<object> Release(LeaseRequest request, CancellationToken ct)
    {
        // 접속 권한·저장 리비전·멱등 키 처리
        Rules.Identifiers(request.PlayerId, request.LeaseId);
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var update = Command(
            connection, null,
            "UPDATE profiles SET lease_id=NULL,lease_server=NULL,lease_expires_at=NULL WHERE player_id=$1 AND lease_id=$2 RETURNING player_id",
            request.PlayerId, request.LeaseId);

        // 접속 권한·저장 리비전·멱등 키 조건 검사
        if (await update.ExecuteScalarAsync(ct) is not Guid)
        {
            throw new ApiError(409, "lease_lost", "이미 종료되었거나 다른 접속의 권한입니다.");
        }

        return new { released = true };
    }

    /// <summary>인증된 플레이어의 현재 프로필·리비전·재화를 조회한다.</summary>
    public async Task<object> Me(Guid player, CancellationToken ct)
    {
        // 접속 권한·저장 리비전·멱등 키에 필요한 값 준비
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var query = Command(
            connection, null,
            $"SELECT revision,{WalletColumns},profile,(lease_id IS NOT NULL AND COALESCE(lease_expires_at>clock_timestamp(),false)) FROM profiles WHERE player_id=$1",
            player);
        await using var reader = await query.ExecuteReaderAsync(ct);

        // SQL 명령과 조회 결과 조건 검사
        if (!await reader.ReadAsync(ct))
        {
            throw new ApiError(404, "player_missing", "플레이어를 찾을 수 없습니다.");
        }

        return new { playerId = player, revision = reader.GetInt64(0), wallet = ReadWallet(reader, 1),
                     profileHash = reader.IsDBNull(8)
                                       ? null
                                       : Convert.ToHexStringLower(SHA256.HashData(reader.GetFieldValue<byte[]>(8))),
                     leaseActive = reader.GetBoolean(9) };
    }

    /// <summary>백엔드 데이터 소스가 보유한 연결 자원을 비동기로 해제한다.</summary>
    public ValueTask DisposeAsync()
    {
        return data.DisposeAsync();
    }
}
