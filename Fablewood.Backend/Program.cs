using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Npgsql;

namespace Fablewood.Backend;

/// <summary>HTTP 보안 경계와 영구 저장 엔드포인트를 구성하는 애플리케이션.</summary>
public static class BackendApplication
{
    /// <summary>백엔드의 HTTP 경계·의존성·속도 제한·엔드포인트를 구성한다.</summary>
    public static WebApplication Build(BackendOptions options)
    {
        options.ValidateCatalog();

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = 512 * 1024);
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.UnmappedMemberHandling =
                                                      JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<SqlStore>();
        builder.Services.AddRateLimiter(
            rate =>
            {
                rate.RejectionStatusCode = 429;
                rate.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                    context => RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 1200, Window = TimeSpan.FromMinutes(1),
                                                                 QueueLimit = 0, AutoReplenishment = true }));
                rate.AddPolicy(
                    "auth",
                    context => RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1),
                                                                 QueueLimit = 0, AutoReplenishment = true }));
                rate.AddPolicy(
                    "tickets",
                    context => RateLimitPartition.GetFixedWindowLimiter(
                        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1),
                                                                 QueueLimit = 0, AutoReplenishment = true }));
            });
        var app = builder.Build();
        var expectedKey = SHA256.HashData(Encoding.UTF8.GetBytes(options.ServerKey));
        app.Use(async (context, next) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.Headers.XContentTypeOptions = "nosniff";
                    try
                    {
                        // Forwarded headers are intentionally not trusted. Terminate TLS
                        // here, or configure a separately reviewed trusted proxy boundary.
                        var address = context.Connection.RemoteIpAddress;
                        if (!context.Request.IsHttps && (address is null || !IPAddress.IsLoopback(address)))
                        {
                            throw new ApiError(403, "https_required", "원격 접속에는 HTTPS가 필요합니다.");
                        }
                        if (context.Request.Path.StartsWithSegments("/v1/server"))
                        {
                            var supplied = context.Request.Headers["X-Fable-Server-Key"];
                            if (supplied.Count != 1 || supplied[0] is not string value || value.Length > 512 ||
                                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(value)),
                                                                         expectedKey))
                            {
                                throw new ApiError(401, "server_unauthorized", "게임 서버 인증이 필요합니다.");
                            }
                        }
                        await next(context);
                    }
                    catch (ApiError error) when (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = error.Status;
                        await context.Response.WriteAsJsonAsync(new { code = error.Code, message = error.Message });
                    }
                    catch (BadHttpRequestException error) when (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = error.StatusCode;
                        await context.Response.WriteAsJsonAsync(
                            new { code = "invalid_request", message = "요청 형식이 올바르지 않습니다." });
                    }
                    catch (Exception error)
                        when ((error is NpgsqlException or TimeoutException) && !context.Response.HasStarted)
                    {
                        // Never serialize SQL text, parameters, connection strings, or secrets.
                        app.Logger.LogWarning("Database operation unavailable ({ErrorType}).", error.GetType().Name);
                        context.Response.StatusCode = 503;
                        await context.Response.WriteAsJsonAsync(
                            new { code = "database_unavailable",
                                  message = "저장 서비스에 연결할 수 없습니다. 잠시 후 다시 시도하세요." });
                    }
                    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                    {
                    }
                    catch (Exception error) when (!context.Response.HasStarted)
                    {
                        app.Logger.LogError("Request failed ({ErrorType}).", error.GetType().Name);
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsJsonAsync(
                            new { code = "internal_error", message = "요청을 처리하지 못했습니다." });
                    }
                });
        app.UseRateLimiter();
        app.MapGet("/health", async (SqlStore store, CancellationToken ct) =>
                              {
                                  await store.Health(ct);
                                  return Results.Ok(new { status = "ok" });
                              });
        app.MapPost("/v1/accounts",
                    async (AccountRequest request, SqlStore store, CancellationToken ct) =>
                        Results.Created("/v1/me", new { playerId = await store.CreateAccount(request, ct) }))
            .RequireRateLimiting("auth");
        app.MapPost("/v1/sessions", async (SessionRequest request, SqlStore store, CancellationToken ct) =>
                                        Results.Ok(await store.CreateSession(request, ct)))
            .RequireRateLimiting("auth");
        app.MapPost("/v1/join-tickets", async (HttpContext context, SqlStore store, CancellationToken ct) =>
                                            Results.Ok(await store.IssueTicket(Bearer(context), ct)))
            .RequireRateLimiting("tickets");
        app.MapPost("/v1/sessions/logout", async (HttpContext context, SqlStore store, CancellationToken ct) =>
                                               Results.Ok(await store.Logout(Bearer(context), ct)));
        app.MapGet("/v1/servers",
                   async (HttpContext context, SqlStore store, CancellationToken ct) =>
                   {
                       await store.Authenticate(Bearer(context), ct);
                       return Results.Ok(
                           new { servers = new[] { new { id = options.GameServerId, name = options.GameServerName,
                                                         address = options.GameAddress } } });
                   });
        app.MapGet("/v1/me/character", async (HttpContext context, SqlStore store, CancellationToken ct) => Results.Ok(
                                           await store.MeCharacter(await store.Authenticate(Bearer(context), ct), ct)));
        app.MapPost("/v1/server/commit-character",
                    async (CommitCharacterRequest request, SqlStore store, CancellationToken ct) =>
                        Results.Ok(await store.CommitCharacter(request, ct)));
        app.MapPost("/v1/server/release-confirmed",
                    async (LeaseRequest request, SqlStore store, CancellationToken ct) =>
                        Results.Ok(await store.ReleaseConfirmed(request, ct)));
        app.MapGet("/v1/me", async (HttpContext context, SqlStore store, CancellationToken ct) =>
                                 Results.Ok(await store.Me(await store.Authenticate(Bearer(context), ct), ct)));
        app.MapPost("/v1/server/claim", async (ClaimRequest request, SqlStore store, CancellationToken ct) =>
                                            Results.Ok(await store.Claim(request, ct)));
        app.MapPost("/v1/server/commit", async (CommitRequest request, SqlStore store, CancellationToken ct) =>
                                             Results.Ok(await store.Commit(request, ct)));
        app.MapPost("/v1/server/commit-inventory",
                    async (CommitInventoryRequest request, SqlStore store, CancellationToken ct) =>
                        Results.Ok(await store.CommitInventory(request, ct)));
        app.MapGet("/v1/me/inventory", async (HttpContext context, SqlStore store, CancellationToken ct) => Results.Ok(
                                           await store.MeInventory(await store.Authenticate(Bearer(context), ct), ct)));
        app.MapPost("/v1/server/heartbeat", async (LeaseRequest request, SqlStore store, CancellationToken ct) =>
                                                Results.Ok(await store.Heartbeat(request, ct)));
        app.MapPost("/v1/server/release", async (LeaseRequest request, SqlStore store, CancellationToken ct) =>
                                              Results.Ok(await store.Release(request, ct)));

        return app;
    }

    /// <summary>HTTP 요청에서 단일 Bearer 인증 토큰을 추출한다.</summary>
    private static string? Bearer(HttpContext context)
    {
        var header = context.Request.Headers.Authorization;

        // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
        if (header.Count != 1 || header[0] is not string value ||
            !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiError(401, "login_required", "로그인이 필요합니다.");
        }

        return value[7..];
    }
}

/// <summary>백엔드 또는 격리 통합 검증의 실행 진입점.</summary>
public static class Program
{
    /// <summary>실행 인자에 맞는 서버 또는 검증 작업을 시작하고 종료 코드를 반환한다.</summary>
    public static async Task<int> Main(string[] args)
    {
        try
        {
            // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
            if (args.Length != 0 && !(args.Length == 1 && args[0] == "--migrate"))
            {
                throw new InvalidOperationException(
                    "Only --migrate is supported; configure credentials through private environment/files.");
            }

            var options = BackendOptions.FromEnvironment();

            // 요청 인자와 현재 진행 상태의 선행 조건을 확인한다.
            if (args.Length == 1)
            {
                await using var store = new SqlStore(options);
                await store.Migrate(CancellationToken.None);
                Console.WriteLine("Database schema version 3 ready.");
                return 0;
            }

            await using var app = BackendApplication.Build(options);
            await app.RunAsync();

            return 0;
        }
        catch (Exception error)
        {
            // Exception messages from drivers/config parsers can contain credentials.
            Console.Error.WriteLine("Backend startup failed (" + error.GetType().Name +
                                    "). Check private configuration and database readiness.");
            return 1;
        }
    }
}
