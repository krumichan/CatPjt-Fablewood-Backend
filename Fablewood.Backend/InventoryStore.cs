using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Fablewood.Backend;

public sealed partial class SqlStore
{
    private static readonly JsonSerializerOptions InventoryJson = new(JsonSerializerDefaults.Web);
    private sealed record InventoryIdentity(Guid InstanceId, Guid Owner, int LocalId, bool Retired);
    private static ApiError InventoryConflict() => new(409, "inventory_conflict", "아이템 식별자 또는 소유 이력이 현재 저장과 일치하지 않습니다.");
    private static ApiError StoredInventoryInvalid() => new(503, "inventory_unavailable", "저장된 장비 정보를 확인할 수 없습니다.");

    private static async Task<InventorySnapshot?> LoadInventory(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid player, int version, CancellationToken ct)
    {
        if (version == 0) return null;
        if (version != 1) throw StoredInventoryInvalid();
        int heroClass, nextLocalId; Guid weapon, armor;
        await using (var query = Command(connection, transaction, "SELECT hero_class,next_local_id,equipped_weapon_instance_id,equipped_armor_instance_id FROM profiles WHERE player_id=$1", player))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct) || reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3)) throw StoredInventoryInvalid();
            heroClass = reader.GetInt32(0); nextLocalId = reader.GetInt32(1); weapon = reader.GetGuid(2); armor = reader.GetGuid(3);
        }
        var items = new List<InventoryItem>();
        await using (var query = Command(connection, transaction, "SELECT instance_id,local_id,location,position,slot,hero_class,rarity,power,upgrade,roll_version,quality,nominal_power,roll_percent,set_id,durability_version,durability,max_durability FROM inventory_items WHERE owner_player_id=$1 AND NOT retired ORDER BY location,position", player))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                items.Add(new(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), reader.GetInt32(13), reader.GetInt32(14), reader.GetInt32(15), reader.GetInt32(16), []));
        }
        var affixes = items.ToDictionary(item => item.InstanceId, _ => new List<ItemAffix>());
        await using (var query = Command(connection, transaction, "SELECT a.instance_id,a.affix_index,a.stat,a.value FROM inventory_affixes a JOIN inventory_items i ON i.instance_id=a.instance_id WHERE i.owner_player_id=$1 AND NOT i.retired ORDER BY i.location,i.position,a.affix_index", player))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                if (!affixes.TryGetValue(reader.GetGuid(0), out var list) || reader.GetInt32(1) != list.Count) throw StoredInventoryInvalid();
                list.Add(new(reader.GetInt32(2), reader.GetInt32(3)));
            }
        }
        var snapshot = new InventorySnapshot(1, heroClass, nextLocalId, weapon, armor, items.Select(item => item with { Affixes = affixes[item.InstanceId].ToArray() }).ToArray());
        try { Rules.ValidateInventory(snapshot); }
        catch (ApiError) { throw StoredInventoryInvalid(); }
        return snapshot;
    }

    public async Task<object> MeInventory(Guid player, CancellationToken ct)
    {
        await using var connection = await data.OpenConnectionAsync(ct);
        // All reads see one committed revision, without taking a writer lease or a row lock.
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        long revision; string? profileHash; int inventoryVersion;
        await using (var query = Command(connection, transaction, "SELECT revision,profile,inventory_version FROM profiles WHERE player_id=$1", player))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) throw new ApiError(404, "player_missing", "플레이어를 찾을 수 없습니다.");
            revision = reader.GetInt64(0);
            profileHash = reader.IsDBNull(1) ? null : Convert.ToHexStringLower(SHA256.HashData(reader.GetFieldValue<byte[]>(1)));
            inventoryVersion = reader.GetInt32(2);
        }
        var inventory = await LoadInventory(connection, transaction, player, inventoryVersion, ct);
        await transaction.CommitAsync(ct);
        return new { playerId = player, revision, profileHash, inventoryVersion, inventory };
    }

    public async Task<CommitInventoryResponse> CommitInventory(CommitInventoryRequest request, CancellationToken ct)
    {
        Rules.Identifiers(request.PlayerId, request.LeaseId, request.OperationId);
        if (request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue || request.Wallet is null)
            throw new ApiError(400, "invalid_commit", "저장 요청이 올바르지 않습니다.");
        request.Wallet.Validate(); Rules.ValidateInventory(request.Inventory);
        var snapshot = request.Inventory;
        var blob = Rules.Profile(request.Profile); var hash = Rules.InventoryRequestHash(request);
        string profileHash = Convert.ToHexStringLower(SHA256.HashData(blob));
        await using var connection = await data.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var profile = await LockedProfile(connection, transaction, request.PlayerId, ct);
        await using (var prior = Command(connection, transaction, "SELECT request_hash,revision FROM operations WHERE player_id=$1 AND operation_id=$2", request.PlayerId, request.OperationId))
        await using (var reader = await prior.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                if (!CryptographicOperations.FixedTimeEquals(hash, reader.GetFieldValue<byte[]>(0)))
                    throw new ApiError(409, "operation_conflict", "같은 작업 식별자에 다른 저장 내용이 전달되었습니다.");
                // A receipt replay does not modify state and remains safe after lease expiry.
                return new(reader.GetInt64(1), profileHash);
            }
        }
        if (profile.LeaseId != request.LeaseId || !profile.LeaseActive)
            throw new ApiError(409, "lease_lost", "저장 권한이 만료되었거나 다른 접속으로 바뀌었습니다.");
        if (profile.Revision != request.ExpectedRevision) throw new ApiError(409, "revision_conflict", "더 최신 저장이 존재합니다.");
        var before = await LoadInventory(connection, transaction, request.PlayerId, profile.InventoryVersion, ct);
        if (before is not null && snapshot.NextLocalId < before.NextLocalId) throw InventoryConflict();

        // Keep all identity tombstones. The profile lock serializes every mutation for this owner.
        // Foreign UUIDs are read only; a concurrent first claim is also fenced by the global PK.
        var known = new Dictionary<Guid, InventoryIdentity>();
        var ownerLocalIds = new HashSet<int>();
        var incomingIds = snapshot.Items.Select(item => item.InstanceId).ToArray();
        await using (var query = Command(connection, transaction, "SELECT instance_id,owner_player_id,local_id,retired FROM inventory_items WHERE owner_player_id=$1 OR instance_id=ANY($2)", request.PlayerId, incomingIds))
        await using (var reader = await query.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var identity = new InventoryIdentity(reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetBoolean(3));
                known.Add(identity.InstanceId, identity);
                if (identity.Owner == request.PlayerId) ownerLocalIds.Add(identity.LocalId);
            }
        }
        foreach (var item in snapshot.Items)
        {
            if (known.TryGetValue(item.InstanceId, out var existing))
            {
                if (existing.Owner != request.PlayerId || existing.Retired || existing.LocalId != item.LocalId) throw InventoryConflict();
            }
            else if (ownerLocalIds.Contains(item.LocalId) || (before is not null && item.LocalId < before.NextLocalId)) throw InventoryConflict();
        }
        long revision = checked(profile.Revision + 1);
        await using (var retire = Command(connection, transaction, "UPDATE inventory_items SET retired=true,location=NULL,position=NULL,updated_revision=$3 WHERE owner_player_id=$1 AND NOT retired AND NOT(instance_id=ANY($2))", request.PlayerId, incomingIds, revision))
            await retire.ExecuteNonQueryAsync(ct);
        try
        {
            foreach (var item in snapshot.Items)
                await WriteInventoryItem(connection, transaction, request.PlayerId, item, revision, known.ContainsKey(item.InstanceId), ct);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Another player's concurrent transaction may have claimed this new UUID first.
            // Disposal rolls back the complete transaction; no owner is ever overwritten.
            throw InventoryConflict();
        }
        var w = request.Wallet;
        await using (var update = Command(connection, transaction, "UPDATE profiles SET revision=revision+1,profile=$4,gold=$5,ore=$6,essence=$7,root_cores=$8,moon_crystals=$9,health_potions=$10,mana_potions=$11,inventory_version=1,hero_class=$12,next_local_id=$13,equipped_weapon_instance_id=$14,equipped_armor_instance_id=$15,updated_at=clock_timestamp() WHERE player_id=$1 AND lease_id=$2 AND revision=$3 AND lease_expires_at>clock_timestamp() RETURNING revision", request.PlayerId, request.LeaseId, request.ExpectedRevision, blob, w.Gold, w.Ore, w.Essence, w.RootCores, w.MoonCrystals, w.HealthPotions, w.ManaPotions, snapshot.HeroClass, snapshot.NextLocalId, snapshot.EquippedWeaponInstanceId, snapshot.EquippedArmorInstanceId))
        {
            // Recheck database-time lease expiry immediately before committing the profile.
            if (await update.ExecuteScalarAsync(ct) is not long actual || actual != revision)
                throw new ApiError(409, "lease_lost", "저장 권한이 만료되었습니다.");
        }
        await using (var operation = Command(connection, transaction, "INSERT INTO operations(player_id,operation_id,request_hash,revision) VALUES($1,$2,$3,$4)", request.PlayerId, request.OperationId, hash, revision)) await operation.ExecuteNonQueryAsync(ct);
        var old = profile.Wallet;
        await using (var ledger = Command(connection, transaction, "INSERT INTO economy_ledger(player_id,operation_id,revision,gold_delta,ore_delta,essence_delta,root_cores_delta,moon_crystals_delta,health_potions_delta,mana_potions_delta) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)", request.PlayerId, request.OperationId, revision, w.Gold-old.Gold, w.Ore-old.Ore, w.Essence-old.Essence, w.RootCores-old.RootCores, w.MoonCrystals-old.MoonCrystals, w.HealthPotions-old.HealthPotions, w.ManaPotions-old.ManaPotions)) await ledger.ExecuteNonQueryAsync(ct);
        await using (var journal = Command(connection, transaction, "INSERT INTO inventory_journal(player_id,operation_id,revision,before_snapshot,after_snapshot) VALUES($1,$2,$3,$4,$5)", request.PlayerId, request.OperationId, revision))
        {
            journal.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = before is null ? DBNull.Value : JsonSerializer.Serialize(before, InventoryJson) });
            journal.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(snapshot, InventoryJson) });
            await journal.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(revision, profileHash);
    }

    private static async Task WriteInventoryItem(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid player, InventoryItem item, long revision, bool exists, CancellationToken ct)
    {
        object[] fields = [item.InstanceId, player, item.LocalId, item.Location, item.Position, item.Slot, item.HeroClass, item.Rarity, item.Power, item.Upgrade, item.RollVersion, item.Quality, item.NominalPower, item.RollPercent, item.SetId, item.DurabilityVersion, item.Durability, item.MaxDurability, revision];
        string sql = exists
            ? "UPDATE inventory_items SET location=$4,position=$5,slot=$6,hero_class=$7,rarity=$8,power=$9,upgrade=$10,roll_version=$11,quality=$12,nominal_power=$13,roll_percent=$14,set_id=$15,durability_version=$16,durability=$17,max_durability=$18,updated_revision=$19 WHERE instance_id=$1 AND owner_player_id=$2 AND local_id=$3 AND NOT retired"
            : "INSERT INTO inventory_items(instance_id,owner_player_id,local_id,location,position,slot,hero_class,rarity,power,upgrade,roll_version,quality,nominal_power,roll_percent,set_id,durability_version,durability,max_durability,created_revision,updated_revision) VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$19)";
        await using (var write = Command(connection, transaction, sql, fields))
            if (await write.ExecuteNonQueryAsync(ct) != 1) throw InventoryConflict();
        await using (var clear = Command(connection, transaction, "DELETE FROM inventory_affixes WHERE instance_id=$1", item.InstanceId)) await clear.ExecuteNonQueryAsync(ct);
        for (int i = 0; i < item.Affixes.Length; i++)
        {
            var affix = item.Affixes[i];
            await using var insert = Command(connection, transaction, "INSERT INTO inventory_affixes(instance_id,affix_index,stat,value) VALUES($1,$2,$3,$4)", item.InstanceId, i, affix.Stat, affix.Value);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }
}
