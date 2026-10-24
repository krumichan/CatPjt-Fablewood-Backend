using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fablewood.Backend;

/// <summary>계정 생성에 필요한 이름·비밀번호·시작 직업 요청.</summary>
public sealed record AccountRequest(string Name, string Password, int InitialClass = 0);

/// <summary>계정 이름과 비밀번호를 전달하는 로그인 요청.</summary>
public sealed record SessionRequest(string Name, string Password);

/// <summary>일회용 티켓과 접속 대상 서버를 전달하는 권한 확보 요청.</summary>
public sealed record ClaimRequest(string Ticket, string ServerId);

/// <summary>플레이어와 접속 권한 식별자를 전달하는 요청.</summary>
public sealed record LeaseRequest(Guid PlayerId, Guid LeaseId);

/// <summary>프로필·재화·리비전·멱등 키를 전달하는 저장 요청.</summary>
public sealed record CommitRequest(Guid PlayerId, Guid LeaseId, Guid OperationId, long ExpectedRevision, string Profile,
                                   Wallet Wallet);

/// <summary>골드·재료·물약 수량을 묶는 저장 계약.</summary>
public sealed record Wallet(int Gold, int Ore, int Essence, int RootCores, int MoonCrystals, int HealthPotions,
                            int ManaPotions)
{
    public static Wallet Initial => new(0, 0, 0, 0, 0, 3, 2);

    /// <summary>재화와 물약의 각 수량이 허용 범위 안인지 검사한다.</summary>
    public void Validate()
    {
        if (Gold is < 0 or > 1_000_000 || Ore is < 0 or > 100_000 || Essence is < 0 or > 100_000 ||
            RootCores is < 0 or > 100_000 || MoonCrystals is < 0 or > 100_000 || HealthPotions is < 0 or > 20 ||
            ManaPotions is < 0 or > 20)
        {
            throw new ApiError(400, "invalid_wallet", "재화 또는 물약 수량이 허용 범위를 벗어났습니다.");
        }
    }
}

/// <summary>서버가 확보한 접속 권한과 현재 영구 프로필의 응답.</summary>
public sealed record ClaimResponse(Guid PlayerId, Guid LeaseId, DateTime LeaseExpiry, long Revision, int InitialClass,
                                   string? Profile, string? ProfileHash, Wallet Wallet, int InventoryVersion = 0,
                                   InventorySnapshot? Inventory = null, CharacterSummary? Summary = null);

/// <summary>확정된 프로필 리비전과 해시의 저장 응답.</summary>
public sealed record CommitResponse(long Revision, string ProfileHash);

/// <summary>클라이언트에 공개 가능한 상태 코드·오류 코드·안내 문구.</summary>
public sealed class ApiError(int status, string code, string message) : Exception
(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

/// <summary>인증 입력과 프로필 저장 계약의 공통 검증 규칙.</summary>
public static partial class Rules
{
    public const int PasswordIterations = 600_000;
    public const int MaxProfileBytes = 256 * 1024;

    /// <summary>계정 이름 검증에 사용하는 정규식 인스턴스를 제공한다.</summary>
    [GeneratedRegex("^[A-Za-z0-9_]{3,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>서버 식별자 검증에 사용하는 정규식 인스턴스를 제공한다.</summary>
    [GeneratedRegex("^[A-Za-z0-9_.-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ServerPattern();

    /// <summary>인증 토큰 검증에 사용하는 정규식 인스턴스를 제공한다.</summary>
    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex TokenPattern();

    /// <summary>허용한 계정 이름 형식을 확인하고 비교용 소문자로 정규화한다.</summary>
    public static string Name(string? name)
    {
        if (name is null || !NamePattern().IsMatch(name))
        {
            throw new ApiError(400, "invalid_name", "계정 이름은 영문, 숫자, 밑줄 3~32자로 입력하세요.");
        }
        return name.ToLowerInvariant();
    }

    /// <summary>비밀번호의 필수 길이 조건을 검사한다.</summary>
    public static void Password(string? password)
    {
        if (password is null || password.Length is < 10 or > 128)
        {
            throw new ApiError(400, "invalid_password", "비밀번호는 10~128자로 입력하세요.");
        }
    }

    /// <summary>게임 서버 식별자가 허용한 형식인지 검사한다.</summary>
    public static void Server(string? serverId)
    {
        if (serverId is null || !ServerPattern().IsMatch(serverId))
        {
            throw new ApiError(400, "invalid_server", "서버 식별자가 올바르지 않습니다.");
        }
    }

    /// <summary>암호학적으로 안전한 난수로 URL 전송 가능한 토큰을 만든다.</summary>
    public static string Token()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>토큰 형식을 확인하고 저장·비교에 사용할 해시를 계산한다.</summary>
    public static byte[] TokenHash(string? token)
    {
        if (token is null || !TokenPattern().IsMatch(token))
        {
            throw new ApiError(401, "invalid_token", "인증 정보가 올바르지 않습니다.");
        }
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    /// <summary>개별 솔트와 정해진 반복 횟수로 비밀번호 해시를 계산한다.</summary>
    public static byte[] PasswordHash(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, 32);
    }

    /// <summary>프로필의 크기와 정규 Base64 인코딩을 검사해 바이트로 반환한다.</summary>
    public static byte[] Profile(string? value)
    {
        // 프로필과 저장 스냅숏 조건 검사
        if (value is null || value.Length == 0 || value.Length > ((MaxProfileBytes + 2) / 3) * 4)
        {
            throw new ApiError(400, "invalid_profile", "프로필 크기가 올바르지 않습니다.");
        }

        byte[] decoded;

        // 이 단계에서 프로필의 크기와 정규 Base64 인코딩을 검사해 바이트로 반환한다.
        try
        {
            decoded = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new ApiError(400, "invalid_profile", "프로필 인코딩이 올바르지 않습니다.");
        }

        // 프로필과 저장 스냅숏 조건 검사
        if (decoded.Length == 0 || decoded.Length > MaxProfileBytes || Convert.ToBase64String(decoded) != value)
        {
            throw new ApiError(400, "invalid_profile", "정규 Base64 프로필이 필요합니다.");
        }

        return decoded;
    }

    /// <summary>저장 요청을 직렬화해 멱등성 비교용 해시를 계산한다.</summary>
    public static byte[] RequestHash(CommitRequest request)
    {
        return SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request));
    }

    /// <summary>요청 식별자에 비어 있는 GUID가 없는지 검사한다.</summary>
    public static void Identifiers(params Guid[] values)
    {
        if (values.Any(value => value == Guid.Empty))
        {
            throw new ApiError(400, "invalid_identifier", "식별자가 올바르지 않습니다.");
        }
    }
}
