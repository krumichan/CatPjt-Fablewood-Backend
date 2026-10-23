using System.Text.RegularExpressions;
using Npgsql;
namespace Fablewood.Backend;
public sealed record BackendOptions(string ConnectionString, string Schema, string ServerKey)
{
    public static BackendOptions FromEnvironment()
    {
        var connection = Environment.GetEnvironmentVariable("FABLE_DB_CONNECTION");
        var schema = Environment.GetEnvironmentVariable("FABLE_DB_SCHEMA") ?? "fablewood";
        var key = Environment.GetEnvironmentVariable("FABLE_SERVER_KEY");
        var keyFile = Environment.GetEnvironmentVariable("FABLE_SERVER_KEY_FILE");
        if (key is not null && keyFile is not null) throw new InvalidOperationException("Set only one server-key source.");
        if (keyFile is not null) key = File.ReadAllText(keyFile).Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || key.Length > 512 || key.Any(char.IsControl)) throw new InvalidOperationException("A private server key of 32 to 512 characters is required.");
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("FABLE_DB_CONNECTION is required.");
        if (!Regex.IsMatch(schema, "^[a-z][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)) throw new InvalidOperationException("Invalid database schema name.");
        var builder = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema, IncludeErrorDetail = false, LogParameters = false };
        // No server key, password or connection string is logged or accepted in CLI arguments.
        return new(builder.ConnectionString, schema, key);
    }
}
