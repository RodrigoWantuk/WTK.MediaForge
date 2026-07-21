using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WTK.MediaForge.Remote.Signaling;

public static class RemoteSceneSignalingHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var options = new RemoteSceneSignalingOptions();
        builder.Configuration.GetSection(RemoteSceneSignalingOptions.SectionName).Bind(options);
        options.Validate();

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SqliteRemoteSceneSessionStore>(_ => new(options.DatabasePath));
        builder.Services.AddSingleton<IRemoteSceneSessionStore>(
            static services => services.GetRequiredService<SqliteRemoteSceneSessionStore>());
        builder.Services.AddSingleton<ITurnCredentialIssuer>(_ => CreateTurnIssuer(options));
        builder.Services.AddSingleton<RemoteSceneInvitationService>();
        builder.Services.AddSingleton<RemoteSceneSignalingRelay>();
        builder.Services.AddHostedService<ExpiredSessionCleanupService>();
        builder.Services.AddRateLimiter(rateLimiter =>
        {
            rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            rateLimiter.AddPolicy("invitation", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    GetClientPartition(context),
                    static _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        QueueLimit = 0,
                        Window = TimeSpan.FromMinutes(1),
                        AutoReplenishment = true
                    }));
            rateLimiter.AddPolicy("signaling", context =>
                RateLimitPartition.GetConcurrencyLimiter(
                    GetClientPartition(context),
                    static _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = 8,
                        QueueLimit = 0
                    }));
        });

        var app = builder.Build();
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(20)
        });

        app.MapGet("/health", static () => Results.Ok(new { status = "healthy", mediaTransport = false }));
        app.MapPost("/v1/invitations", CreateInvitationAsync)
            .RequireRateLimiting("invitation");
        app.MapPost("/v1/invitations/redeem", RedeemInvitationAsync)
            .RequireRateLimiting("invitation");
        app.MapGet("/v1/sessions/{sessionId:guid}/signal", RunSignalingSocketAsync)
            .RequireRateLimiting("signaling");

        return app;
    }

    private static async Task<IResult> CreateInvitationAsync(
        HttpContext context,
        CreateRemoteSceneInvitationRequest request,
        RemoteSceneInvitationService invitations,
        RemoteSceneSignalingOptions options,
        CancellationToken cancellationToken)
    {
        if (!IsSecureTransport(context, options))
            return Results.Problem("HTTPS is required.", statusCode: StatusCodes.Status426UpgradeRequired);
        if (!TryReadBearerToken(context.Request, out var bearer) ||
            !RemoteSceneSecret.FixedTimeEquals(options.AdminBearerToken, bearer))
        {
            return Results.Unauthorized();
        }

        try
        {
            return Results.Ok(await invitations.CreateAsync(request, cancellationToken).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [ex.Message] });
        }
    }

    private static async Task<IResult> RedeemInvitationAsync(
        HttpContext context,
        RedeemRemoteSceneInvitationRequest request,
        RemoteSceneInvitationService invitations,
        RemoteSceneSignalingOptions options,
        CancellationToken cancellationToken)
    {
        if (!IsSecureTransport(context, options))
            return Results.Problem("HTTPS is required.", statusCode: StatusCodes.Status426UpgradeRequired);

        try
        {
            var result = await invitations.RedeemAsync(request, cancellationToken).ConfigureAwait(false);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (ArgumentException)
        {
            return Results.NotFound();
        }
    }

    private static async Task RunSignalingSocketAsync(
        HttpContext context,
        Guid sessionId,
        RemoteSceneInvitationService invitations,
        RemoteSceneSignalingRelay relay,
        RemoteSceneSignalingOptions options,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!IsSecureTransport(context, options))
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!TryReadBearerToken(context.Request, out var accessToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        RemoteSceneSessionAccess? access;
        try
        {
            access = await invitations.AuthorizeAsync(sessionId, accessToken, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            access = null;
        }

        if (access is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        try
        {
            await relay.RunAsync(access, socket, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("RemoteSceneSignaling")
                .LogWarning(ex, "Remote Scene signaling session {SessionId} ended with a failure.", sessionId);
        }
    }

    private static ITurnCredentialIssuer CreateTurnIssuer(RemoteSceneSignalingOptions options)
    {
        if (options.TurnUrls.Length == 0)
            return NoTurnCredentialIssuer.Instance;

        return new TurnRestCredentialIssuer(
            options.TurnUrls.Select(static value => new Uri(value, UriKind.Absolute)),
            options.TurnSharedSecret);
    }

    private static bool IsSecureTransport(HttpContext context, RemoteSceneSignalingOptions options) =>
        context.Request.IsHttps ||
        (options.AllowInsecureDevelopmentTransport && context.Request.Host.Host is "localhost" or "127.0.0.1" or "::1");

    private static bool TryReadBearerToken(HttpRequest request, out string token)
    {
        const string prefix = "Bearer ";
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            token = string.Empty;
            return false;
        }

        token = authorization[prefix.Length..].Trim();
        return token.Length > 0;
    }

    private static string GetClientPartition(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString();
}
