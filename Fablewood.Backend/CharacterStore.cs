using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Fablewood.Backend;

public sealed partial class SqlStore
{
    private static async Task<CharacterSummary?> LoadSummary(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid player, CancellationToken ct)
    {
        await using var query = Command(connection, transaction, "SELECT s.version,s.hero_class,s.level,s.experience,s.class_tier,p.inventory_version,p.hero_class FROM character_summaries s JOIN profiles p USING(player_id) WHERE s.player_id=$1", player);
        await using var reader = await query.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var summary = new CharacterSummary(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
        try
        {
            if (reader.GetInt32(5) != 1 || reader.IsDBNull(6)) throw new ApiError(400, "invalid_summary", "Invalid stored projection.");
            Rules.ValidateSummary(summary, reader.GetInt32(6));
        }
        catch (ApiError) { throw new ApiError(503, "character_unavailable", "저장된 캐릭터 정보를 확인할 수 없습니다."); }
        return summary;
    }
    public async Task<CommitCharacterResponse> CommitCharacter(CommitCharacterRequest request, CancellationToken ct)
    {
        Rules.ValidateInventory(request.Inventory);
        Rules.ValidateSummary(request.Summary, request.Inventory.HeroClass);
        var inventoryRequest = new CommitInventoryRequest(request.PlayerId, request.LeaseId, request.OperationId,
            request.ExpectedRevision, request.Profile, request.Wallet, request.Inventory);
        var ack = await CommitInventoryCore(inventoryRequest, Rules.CharacterRequestHash(request), request.Summary, ct);
        return new(ack.Revision, ack.ProfileHash);
    }
    private static async Task WriteSummary(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid player, Guid operation, long revision,
        CharacterSummary? before, CharacterSummary after, CancellationToken ct)
    {
        await using (var update = Command(connection, transaction, "INSERT INTO character_summaries(player_id,version,hero_class,level,experience,class_tier) VALUES($1,$2,$3,$4,$5,$6) ON CONFLICT(player_id) DO UPDATE SET version=EXCLUDED.version,hero_class=EXCLUDED.hero_class,level=EXCLUDED.level,experience=EXCLUDED.experience,class_tier=EXCLUDED.class_tier", player, after.Version, after.HeroClass, after.Level, after.Experience, after.ClassTier))
            await update.ExecuteNonQueryAsync(ct);
        await using var journal = Command(connection, transaction, "INSERT INTO character_journal(player_id,operation_id,revision,before_summary,after_summary) VALUES($1,$2,$3,$4,$5)", player, operation, revision);
        journal.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = before is null ? DBNull.Value : JsonSerializer.Serialize(before, InventoryJson) });
        journal.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(after, InventoryJson) });
        await journal.ExecuteNonQueryAsync(ct);
    }
    public async Task<object> MeCharacter(Guid player, CancellationToken ct)
    {
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        string name; long revision; string? profileHash; int initialClass, inventoryVersion; Wallet wallet;
        await using (var query = Command(connection, transaction, "SELECT a.name,a.initial_class,p.revision,p.profile,p.gold,p.ore,p.essence,p.root_cores,p.moon_crystals,p.health_potions,p.mana_potions,p.inventory_version FROM profiles p JOIN accounts a USING(player_id) WHERE p.player_id=$1", player))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiError(404, "player_missing", "플레이어를 찾을 수 없습니다.");
            name = reader.GetString(0); initialClass = reader.GetInt32(1); revision = reader.GetInt64(2);
            profileHash = reader.IsDBNull(3) ? null : Convert.ToHexStringLower(SHA256.HashData(reader.GetFieldValue<byte[]>(3)));
            wallet = ReadWallet(reader, 4); inventoryVersion = reader.GetInt32(11);
        }
        var inventory = await LoadInventory(connection, transaction, player, inventoryVersion, ct);
        var summary = await LoadSummary(connection, transaction, player, ct);
        await transaction.CommitAsync(ct);
        return new { name, playerId = player, revision, profileHash, initialClass, wallet, inventoryVersion, inventory, summary };
    }
    private static async Task<Guid> LockSession(NpgsqlConnection connection, NpgsqlTransaction transaction, byte[] hash, CancellationToken ct)
    {
        await using var query = Command(connection, transaction, "SELECT player_id FROM sessions WHERE token_hash=$1 AND expires_at>clock_timestamp() FOR UPDATE", hash);
        return await query.ExecuteScalarAsync(ct) is Guid player ? player : throw new ApiError(401, "session_expired", "로그인이 만료되었습니다.");
    }
    public async Task<object> Logout(string? token, CancellationToken ct)
    {
        var hash = Rules.TokenHash(token);
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await LockSession(connection, transaction, hash, ct);
        await using (var session = Command(connection, transaction, "UPDATE sessions SET expires_at=LEAST(expires_at,clock_timestamp()) WHERE token_hash=$1", hash)) await session.ExecuteNonQueryAsync(ct);
        await using (var tickets = Command(connection, transaction, "UPDATE join_tickets SET expires_at=LEAST(expires_at,clock_timestamp()) WHERE session_token_hash=$1 AND consumed_at IS NULL", hash)) await tickets.ExecuteNonQueryAsync(ct);
        // A claimed lease belongs to the game server. Do not interrupt pending durable saves.
        await transaction.CommitAsync(ct); return new { loggedOut = true };
    }
    public async Task<object> ReleaseConfirmed(LeaseRequest request, CancellationToken ct)
    {
        Rules.Identifiers(request.PlayerId, request.LeaseId);
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var profile = await LockedProfile(connection, transaction, request.PlayerId, ct);
        await using (var receipt = Command(connection, transaction, "SELECT 1 FROM released_leases WHERE player_id=$1 AND lease_id=$2", request.PlayerId, request.LeaseId))
            if (await receipt.ExecuteScalarAsync(ct) is not null) return new { released = true };
        if (profile.LeaseId != request.LeaseId || !profile.LeaseActive) throw new ApiError(409, "lease_lost", "접속 권한이 이미 만료되었거나 다른 접속으로 바뀌었습니다.");
        await using (var release = Command(connection, transaction, "UPDATE profiles SET lease_id=NULL,lease_server=NULL,lease_expires_at=NULL WHERE player_id=$1 AND lease_id=$2 AND lease_expires_at>clock_timestamp() RETURNING player_id", request.PlayerId, request.LeaseId))
            if (await release.ExecuteScalarAsync(ct) is not Guid) throw new ApiError(409, "lease_lost", "접속 권한이 만료되었습니다.");
        await using (var receipt = Command(connection, transaction, "INSERT INTO released_leases(player_id,lease_id) VALUES($1,$2)", request.PlayerId, request.LeaseId)) await receipt.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct); return new { released = true };
    }
}
