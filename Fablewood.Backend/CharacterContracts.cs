using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fablewood.Backend;

/// <summary>직업·레벨·경험치·전직 단계로 구성한 공개 캐릭터 요약.</summary>
public sealed record CharacterSummary([property : JsonRequired] int Version, [property : JsonRequired] int HeroClass,
                                      [property : JsonRequired] int Level, [property : JsonRequired] int Experience,
                                      [property : JsonRequired] int ClassTier);

/// <summary>캐릭터 요약과 인벤토리를 함께 전달하는 저장 요청.</summary>
public sealed record CommitCharacterRequest(
    [property : JsonRequired] Guid PlayerId, [property : JsonRequired] Guid LeaseId,
    [property : JsonRequired] Guid OperationId, [property : JsonRequired] long ExpectedRevision,
    [property : JsonRequired] string Profile, [property : JsonRequired] Wallet Wallet,
    [property : JsonRequired] InventorySnapshot Inventory, [property : JsonRequired] CharacterSummary Summary);

/// <summary>캐릭터 요약까지 저장한 리비전과 해시 응답.</summary>
public sealed record CommitCharacterResponse(long Revision, string ProfileHash, int InventoryVersion = 1,
                                             int SummaryVersion = 1);

/// <summary>인증 입력과 프로필 저장 계약의 공통 검증 규칙.</summary>
public static partial class Rules
{
    /// <summary>캐릭터 요약을 포함한 저장 요청의 전체 계약을 해시로 묶는다.</summary>
    public static byte[] CharacterRequestHash(CommitCharacterRequest request)
    {
        return SHA256.HashData(
            Encoding.UTF8.GetBytes("fablewood-character-commit-v1\n" + JsonSerializer.Serialize(request)));
    }

    /// <summary>캐릭터 요약의 버전·직업·성장 수치가 허용한 계약에 맞는지 검사한다.</summary>
    public static void ValidateSummary(CharacterSummary? summary, int heroClass)
    {
        if (summary is null || summary.Version != 1 || summary.HeroClass is < 0 or > 2 ||
            summary.HeroClass != heroClass || summary.Level is < 1 or > 20 || summary.Experience < 0 ||
            (summary.Level < 20 ? summary.Experience >= summary.Level * 100 : summary.Experience != 0) ||
            summary.ClassTier is < 1 or > 3 || (summary.ClassTier >= 2 && summary.Level < 2) ||
            (summary.ClassTier >= 3 && summary.Level < 4))
        {
            throw new ApiError(400, "invalid_summary", "캐릭터 정보가 허용 범위를 벗어났습니다.");
        }
    }
}
