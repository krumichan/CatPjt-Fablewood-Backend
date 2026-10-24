using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fablewood.Backend;

/// <summary>저장 계약에 사용하는 추가 옵션 종류와 수치.</summary>
public sealed record ItemAffix([property : JsonRequired] int Stat, [property : JsonRequired] int Value);

/// <summary>SQL과 API가 공유하는 아이템의 전체 저장 속성.</summary>
public sealed record InventoryItem([property : JsonRequired] Guid InstanceId, [property : JsonRequired] int LocalId,
                                   [property : JsonRequired] int Location, [property : JsonRequired] int Position,
                                   [property : JsonRequired] int Slot, [property : JsonRequired] int HeroClass,
                                   [property : JsonRequired] int Rarity, [property : JsonRequired] int Power,
                                   [property : JsonRequired] int Upgrade, [property : JsonRequired] int RollVersion,
                                   [property : JsonRequired] int Quality, [property : JsonRequired] int NominalPower,
                                   [property : JsonRequired] int RollPercent, [property : JsonRequired] int SetId,
                                   [property : JsonRequired] int DurabilityVersion,
                                   [property : JsonRequired] int Durability,
                                   [property : JsonRequired] int MaxDurability,
                                   [property : JsonRequired] ItemAffix[] Affixes);

/// <summary>아이템·옵션·창고·착용 식별자를 묶는 인벤토리 저장 계약.</summary>
public sealed record InventorySnapshot([property : JsonRequired] int Version, [property : JsonRequired] int HeroClass,
                                       [property : JsonRequired] int NextLocalId,
                                       [property : JsonRequired] Guid EquippedWeaponInstanceId,
                                       [property : JsonRequired] Guid EquippedArmorInstanceId,
                                       [property : JsonRequired] InventoryItem[] Items);

/// <summary>인벤토리 계약을 포함한 프로필 저장 요청.</summary>
public sealed record CommitInventoryRequest([property : JsonRequired] Guid PlayerId,
                                            [property : JsonRequired] Guid LeaseId,
                                            [property : JsonRequired] Guid OperationId,
                                            [property : JsonRequired] long ExpectedRevision,
                                            [property : JsonRequired] string Profile,
                                            [property : JsonRequired] Wallet Wallet,
                                            [property : JsonRequired] InventorySnapshot Inventory);

/// <summary>확정된 인벤토리 저장의 리비전과 해시 응답.</summary>
public sealed record CommitInventoryResponse([property : JsonRequired] long Revision,
                                             [property : JsonRequired] string ProfileHash,
                                             [property : JsonRequired] int InventoryVersion = 1);

/// <summary>인증 입력과 프로필 저장 계약의 공통 검증 규칙.</summary>
public static partial class Rules
{
    /// <summary>인벤토리 저장 요청의 전체 계약을 해시로 묶는다.</summary>
    public static byte[] InventoryRequestHash(CommitInventoryRequest request)
    {
        return SHA256.HashData(
            Encoding.UTF8.GetBytes("fablewood-inventory-commit-v1\n" + JsonSerializer.Serialize(request)));
    }

    /// <summary>아이템 식별자·착용·품질·옵션·내구도의 전체 저장 계약을 검사한다.</summary>
    public static void ValidateInventory(InventorySnapshot? snapshot)
    {
        /// <summary>검증 조건을 만족하지 않는 요청을 계약 오류로 거부한다.</summary>
        // 아이템·장비 옵션·인벤토리와 창고 처리
        static void Require(bool valid)
        {
            if (!valid)
            {
                throw new ApiError(400, "invalid_inventory", "장비 소유 목록 또는 성능 값이 올바르지 않습니다.");
            }
        }
        Require(snapshot is not null);

        var value = snapshot!;
        Require(value.Version == 1 && value.HeroClass is >= 0 and <= 2 && value.NextLocalId is > 4 and < 1_000_000);
        Require(value.Items is not null && value.Items.Length is >= 4 and <= 80);
        Require(value.EquippedWeaponInstanceId != Guid.Empty && value.EquippedArmorInstanceId != Guid.Empty);

        var instances = new HashSet<Guid>();
        var localIds = new HashSet<int>();
        var starter = new bool[4];
        int inventoryCount = 0, stashCount = 0, priorLocation = 0;
        bool weapon = false, armor = false;

        // 아이템·장비 옵션·인벤토리와 창고 순회
        foreach (var entry in value.Items!)
        {
            // 아이템·장비 옵션·인벤토리와 창고 처리
            Require(entry is not null);
            var item = entry!;
            Require(item.InstanceId != Guid.Empty && instances.Add(item.InstanceId));
            Require(item.LocalId > 0 && item.LocalId < value.NextLocalId && localIds.Add(item.LocalId));
            Require(item.Location is 0 or 1 && item.Location >= priorLocation);
            priorLocation = item.Location;
            int expectedPosition = item.Location == 0 ? inventoryCount++ : stashCount++;
            Require(item.Position == expectedPosition && inventoryCount <= 32 && stashCount <= 48);
            Require(item.Slot is 0 or 1 && item.HeroClass is >= 0 and <= 2 && item.Rarity is >= 0 and <= 4 &&
                    item.Upgrade is >= 0 and <= 5);
            bool legend = item.RollVersion == 2 && item.Rarity == 4;
            Require(item.Power >= 0 && item.Power <= (legend ? 104 : 100));
            Require(item.Affixes is not null && item.Affixes.Length <= 3);

            // 아이템·장비 옵션·인벤토리와 창고 처리
            if (item.RollVersion == 0)
            {
                Require(item.Rarity <= 3 && item.Quality == 0 && item.NominalPower == 0 && item.RollPercent == 100 &&
                        item.SetId == 0 && item.Affixes!.Length == 0);
            }
            else
            {
                // 아이템·장비 옵션·인벤토리와 창고 처리
                Require((item.RollVersion == 1 && item.Rarity is >= 1 and <= 3) || (legend && item.SetId == 0));
                Require(item.LocalId > 4 && item.Quality is >= 1 and <= 4 && item.SetId is >= 0 and <= 2);
                int min = item.Quality switch {
                    1 => 80,
                    2 => 95,
                    3 => 110,
                    4 => 125,
                    _ => 0
                };
                int max = item.Quality switch {
                    1 => 94,
                    2 => 109,
                    3 => 124,
                    4 => 140,
                    _ => 0
                };
                int affixCount = item.Quality == 1 ? 1 : item.Quality == 4 ? 3 : 2;
                Require(item.NominalPower is > 0 and <= 100 && item.RollPercent >= min && item.RollPercent <= max &&
                        item.Affixes!.Length == affixCount);
                // UE RoundToInt rounds positive .5 upward; Math.Round's default is different.
                Require(item.Power == (item.NominalPower * item.RollPercent + 50) / 100);
                bool nominal = item.Rarity switch {
                    1 => item.Slot == 0 ? item.NominalPower is 30 or 32 : item.NominalPower == 6,
                    2 => item.NominalPower == (item.Slot == 0 ? 48 : 12),
                    3 => item.NominalPower == (item.Slot == 0 ? 58 : 16),
                    4 => item.NominalPower == (item.Slot == 0 ? 74 : 22),
                    _ => false
                };
                Require(nominal && (item.SetId != 1 || item.Rarity == 2) && (item.SetId != 2 || item.Rarity == 3));
            }

            var stats = new HashSet<int>();

            // 아이템·장비 옵션·인벤토리와 창고 순회
            foreach (var entryAffix in item.Affixes!)
            {
                Require(entryAffix is not null);
                var affix = entryAffix!;
                Require(stats.Add(affix.Stat) && (affix.Stat switch {
                            0 => affix.Value is >= 2 and <= 6,
                            1 => affix.Value is >= 1 and <= 4,
                            2 => affix.Value is >= 5 and <= 20,
                            3 => affix.Value is >= 5 and <= 15, // Tenths of mana per second, not floating point.
                            _ => false
                        }));
            }

            // 아이템·장비 옵션·인벤토리와 창고에 필요한 값 준비
            int maximum = item.DurabilityVersion == 0 ? 100 : item.DurabilityVersion == 1
                ? item.Rarity switch { 0 => 60, 1 => 80, 2 => 100, 3 => 120, 4 => 150, _ => 0 } : 0;
            Require(maximum > 0 && item.MaxDurability == maximum && item.Durability >= 0 && item.Durability <= maximum);

            // 아이템·장비 옵션·인벤토리와 창고 처리
            if (item.Location == 0)
            {
                if (item.LocalId is >= 1 and <= 3)
                {
                    starter[item.LocalId - 1] = item.Slot == 0 && item.HeroClass == item.LocalId - 1;
                }
                else if (item.LocalId == 4)
                {
                    starter[3] = item.Slot == 1;
                }
                weapon |= item.InstanceId == value.EquippedWeaponInstanceId && item.Slot == 0 &&
                          item.HeroClass == value.HeroClass;
                armor |= item.InstanceId == value.EquippedArmorInstanceId && item.Slot == 1;
            }
            else
            {
                Require(item.LocalId > 4);
            }
        }

        Require(inventoryCount >= 4 && starter.All(x => x) && weapon && armor);
    }
}
