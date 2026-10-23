using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fablewood.Backend;

public sealed record AccountRequest(string Name, string Password, int InitialClass = 0);
public sealed record SessionRequest(string Name, string Password);
public sealed record ClaimRequest(string Ticket, string ServerId);
public sealed record LeaseRequest(Guid PlayerId, Guid LeaseId);
public sealed record CommitRequest(Guid PlayerId, Guid LeaseId, Guid OperationId, long ExpectedRevision, string Profile, Wallet Wallet);
public sealed record Wallet(int Gold, int Ore, int Essence, int RootCores, int MoonCrystals, int HealthPotions, int ManaPotions)
{
    public static Wallet Initial => new(0, 0, 0, 0, 0, 3, 2);
    public void Validate()
    {
        if (Gold is < 0 or > 1_000_000 || Ore is < 0 or > 100_000 || Essence is < 0 or > 100_000 || RootCores is < 0 or > 100_000 || MoonCrystals is < 0 or > 100_000 || HealthPotions is < 0 or > 20 || ManaPotions is < 0 or > 20)
            throw new ApiError(400, "invalid_wallet", "재화 또는 물약 수량이 허용 범위를 벗어났습니다.");
    }
}
public sealed record ClaimResponse(Guid PlayerId, Guid LeaseId, DateTime LeaseExpiry, long Revision, int InitialClass, string? Profile, string? ProfileHash, Wallet Wallet);
public sealed record CommitResponse(long Revision, string ProfileHash);
public sealed class ApiError(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public static partial class Rules
{
    public const int PasswordIterations = 600_000;
    public const int MaxProfileBytes = 256 * 1024;
    [GeneratedRegex("^[A-Za-z0-9_]{3,32}$", RegexOptions.CultureInvariant)] private static partial Regex NamePattern();
    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$", RegexOptions.CultureInvariant)] private static partial Regex ServerPattern();
    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)] private static partial Regex TokenPattern();
    public static string Name(string? name)
    {
        if (name is null || !NamePattern().IsMatch(name)) throw new ApiError(400, "invalid_name", "계정 이름은 영문, 숫자, 밑줄 3~32자로 입력하세요.");
        return name.ToLowerInvariant();
    }
    public static void Password(string? password)
    {
        if (password is null || password.Length is < 10 or > 128) throw new ApiError(400, "invalid_password", "비밀번호는 10~128자로 입력하세요.");
    }
    public static void Server(string? serverId)
    {
        if (serverId is null || !ServerPattern().IsMatch(serverId)) throw new ApiError(400, "invalid_server", "서버 식별자가 올바르지 않습니다.");
    }
    public static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] TokenHash(string? token)
    {
        if (token is null || !TokenPattern().IsMatch(token)) throw new ApiError(401, "invalid_token", "인증 정보가 올바르지 않습니다.");
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
    public static byte[] PasswordHash(string password, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
    public static byte[] Profile(string? value)
    {
        if (value is null || value.Length == 0 || value.Length > ((MaxProfileBytes + 2) / 3) * 4) throw new ApiError(400, "invalid_profile", "프로필 크기가 올바르지 않습니다.");
        byte[] decoded;
        try { decoded = Convert.FromBase64String(value); }
        catch (FormatException) { throw new ApiError(400, "invalid_profile", "프로필 인코딩이 올바르지 않습니다."); }
        if (decoded.Length == 0 || decoded.Length > MaxProfileBytes || Convert.ToBase64String(decoded) != value) throw new ApiError(400, "invalid_profile", "정규 Base64 프로필이 필요합니다.");
        return decoded;
    }
    public static byte[] RequestHash(CommitRequest request) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
    public static void Identifiers(params Guid[] values)
    {
        if (values.Any(value => value == Guid.Empty)) throw new ApiError(400, "invalid_identifier", "식별자가 올바르지 않습니다.");
    }
}
