using System.Globalization;
using System.Text.RegularExpressions;
using Npgsql;
namespace Fablewood.Backend;

/// <summary>백엔드의 비공개 접속 설정과 게임 서버 카탈로그.</summary>
public sealed record BackendOptions(string ConnectionString, string Schema, string ServerKey,
                                    string GameAddress = "127.0.0.1:7821", string GameServerId = "onlineflow-local",
                                    string GameServerName = "동화숲")
{
    /// <summary>접속 대상 서버의 식별자·이름·주소 설정을 검사한다.</summary>
    public void ValidateCatalog()
    {
        // Match UE ValidateJoinAddress's explicit IPv4/DNS:port subset. IPv6/travel URL options are unsupported.
        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (GameAddress is null || GameAddress.Length is < 3 or > 259 ||
            GameAddress.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            throw new InvalidOperationException("Invalid game server address.");
        }

        int colon = GameAddress.IndexOf(':');

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (colon < 1 || colon != GameAddress.LastIndexOf(':'))
        {
            throw new InvalidOperationException("Game address requires one host/port separator.");
        }

        string host = GameAddress[..colon], portText = GameAddress[(colon + 1)..];

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (host.Length > 253 ||
            !Regex.IsMatch(host, @"^[A-Za-z0-9](?:[A-Za-z0-9.-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Invalid game address host.");
        }

        var labels = host.Split('.');

        // 이 단계에서 접속 대상 서버의 식별자·이름·주소 설정을 검사한다.
        if (host.All(c => c == '.' || (c >= '0' && c <= '9')))
        {
            // No abbreviated, out-of-range or leading-zero IPv4 forms; UE rejects them too.
            if (labels.Length != 4 ||
                labels.Any(label =>
                               label.Length is < 1 or > 3 || (label.Length > 1 && label[0] == '0') ||
                               !int.TryParse(label, NumberStyles.None, CultureInfo.InvariantCulture, out var octet) ||
                               octet > 255))
            {
                throw new InvalidOperationException("Invalid IPv4 game address.");
            }
        }
        else if (labels.Any(label => label.Length is < 1 or > 63 || label.StartsWith('-') || label.EndsWith('-')))
        {
            throw new InvalidOperationException("Invalid DNS game address.");
        }

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (portText.Length is < 1 or > 5 || portText.Any(c => c is < '0' or > '9') ||
            !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ||
            port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Invalid game address port.");
        }

        Rules.Server(GameServerId);

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (string.IsNullOrWhiteSpace(GameServerName) || GameServerName.Length > 64 ||
            GameServerName.Any(char.IsControl))
        {
            throw new InvalidOperationException("Invalid game server display name.");
        }
    }

    /// <summary>개발용 환경과 비공개 파일에서 백엔드 설정을 읽는다.</summary>
    public static BackendOptions FromEnvironment()
    {
        var connection = Environment.GetEnvironmentVariable("FABLE_DB_CONNECTION");
        var schema = Environment.GetEnvironmentVariable("FABLE_DB_SCHEMA") ?? "fablewood";
        var key = Environment.GetEnvironmentVariable("FABLE_SERVER_KEY");
        var keyFile = Environment.GetEnvironmentVariable("FABLE_SERVER_KEY_FILE");

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (key is not null && keyFile is not null)
        {
            throw new InvalidOperationException("Set only one server-key source.");
        }

        // 이 단계에서 개발용 환경과 비공개 파일에서 백엔드 설정을 읽는다.
        if (keyFile is not null)
        {
            key = File.ReadAllText(keyFile).Trim();
        }

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || key.Length > 512 || key.Any(char.IsControl))
        {
            throw new InvalidOperationException("A private server key of 32 to 512 characters is required.");
        }
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException("FABLE_DB_CONNECTION is required.");
        }
        if (!Regex.IsMatch(schema, "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException("Invalid database schema name.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, IncludeErrorDetail = false,
                                                                      LogParameters = false };
        // No server key, password or connection string is logged or accepted in CLI arguments.
        var options =
            new BackendOptions(builder.ConnectionString, schema, key,
                               Environment.GetEnvironmentVariable("FABLE_GAME_ADDRESS") ?? "127.0.0.1:7821",
                               Environment.GetEnvironmentVariable("FABLE_GAME_SERVER_ID") ?? "onlineflow-local",
                               Environment.GetEnvironmentVariable("FABLE_GAME_SERVER_NAME") ?? "동화숲");
        options.ValidateCatalog();

        return options;
    }
}
