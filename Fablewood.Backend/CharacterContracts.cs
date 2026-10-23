using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fablewood.Backend;

public sealed record CharacterSummary(
    [property: JsonRequired] int Version,
    [property: JsonRequired] int HeroClass,
    [property: JsonRequired] int Level,
    [property: JsonRequired] int Experience,
    [property: JsonRequired] int ClassTier);
public sealed record CommitCharacterRequest(
    [property: JsonRequired] Guid PlayerId,
    [property: JsonRequired] Guid LeaseId,
    [property: JsonRequired] Guid OperationId,
    [property: JsonRequired] long ExpectedRevision,
    [property: JsonRequired] string Profile,
    [property: JsonRequired] Wallet Wallet,
    [property: JsonRequired] InventorySnapshot Inventory,
    [property: JsonRequired] CharacterSummary Summary);
public sealed record CommitCharacterResponse(long Revision, string ProfileHash, int InventoryVersion = 1, int SummaryVersion = 1);

public static partial class Rules
{
    public static byte[] CharacterRequestHash(CommitCharacterRequest request) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("fablewood-character-commit-v1\n" + JsonSerializer.Serialize(request)));
    public static void ValidateSummary(CharacterSummary? summary, int heroClass)
    {
        if (summary is null || summary.Version != 1 || summary.HeroClass is < 0 or > 2
            || summary.HeroClass != heroClass || summary.Level is < 1 or > 20
            || summary.Experience < 0 || (summary.Level < 20 ? summary.Experience >= summary.Level * 100 : summary.Experience != 0)
            || summary.ClassTier is < 1 or > 3 || (summary.ClassTier >= 2 && summary.Level < 2)
            || (summary.ClassTier >= 3 && summary.Level < 4))
            throw new ApiError(400, "invalid_summary", "캐릭터 정보가 허용 범위를 벗어났습니다.");
    }
}
