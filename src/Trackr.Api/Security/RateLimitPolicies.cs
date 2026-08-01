using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Trackr.Api.Security;

/// <summary>
/// Names of the rate-limiting policies applied to the auth endpoints.
/// </summary>
/// <remarks>
/// CLAUDE.md section 8.3 asks for rate limiting on login, register and 2FA to blunt
/// automated attempts. This is the second line after Identity's account lockout, and it
/// covers the cases lockout cannot: attempts spread across many usernames, and hammering
/// the register endpoint.
/// </remarks>
public static class RateLimitPolicies
{
    /// <summary>Login, 2FA code and recovery code. Tried repeatedly by a real person who mistyped.</summary>
    public const string Login = "auth-login";

    /// <summary>Register, password reset, password change, 2FA changes, invite creation.</summary>
    public const string Sensitive = "auth-sensitive";
}

/// <summary>Bound from the <c>Trackr:RateLimiting</c> configuration section.</summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "Trackr:RateLimiting";

    public int LoginPermitLimit { get; set; } = 10;
    public int LoginWindowSeconds { get; set; } = 60;

    public int SensitivePermitLimit { get; set; } = 5;
    public int SensitiveWindowSeconds { get; set; } = 900;
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddTrackrRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var settings = configuration.GetSection(RateLimitSettings.SectionName).Get<RateLimitSettings>()
            ?? new RateLimitSettings();

        services.AddRateLimiter(options =>
        {
            options.AddPolicy(RateLimitPolicies.Login, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.LoginPermitLimit,
                        Window = TimeSpan.FromSeconds(settings.LoginWindowSeconds),
                        // Reject immediately rather than queueing. A caller waiting in a
                        // queue for a login is worse than a clear 429 telling them when
                        // to come back.
                        QueueLimit = 0
                    }));

            options.AddPolicy(RateLimitPolicies.Sensitive, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(httpContext), _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.SensitivePermitLimit,
                        Window = TimeSpan.FromSeconds(settings.SensitiveWindowSeconds),
                        QueueLimit = 0
                    }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                // The fixed-window limiter populates this, so the caller gets a real
                // number rather than having to guess when to retry.
                var retryAfterSeconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)retryAfter.TotalSeconds
                    : (int)TimeSpan.FromSeconds(settings.LoginWindowSeconds).TotalSeconds;

                context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

                // CLAUDE.md section 5: failures reach the user as a plain message rather
                // than vanishing. problem+json so the client can render it uniformly.
                // The content type goes through this overload rather than being assigned
                // afterwards - writing the body starts the response and makes the headers
                // read-only, so a later assignment throws.
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new
                    {
                        type = "https://tools.ietf.org/html/rfc9110#section-15.5.29",
                        title = "Too many requests.",
                        status = StatusCodes.Status429TooManyRequests,
                        detail = $"Too many attempts. Try again in {retryAfterSeconds} seconds.",
                        retryAfterSeconds
                    },
                    options: null,
                    contentType: "application/problem+json",
                    cancellationToken);
            };
        });

        return services;
    }

    /// <summary>
    /// Partitions the limiter by caller IP.
    /// </summary>
    /// <remarks>
    /// This only sees the real client address because UseForwardedHeaders runs before
    /// UseRateLimiter. Two caveats worth knowing rather than being surprised by:
    /// <list type="bullet">
    /// <item>Behind the user's own reverse proxy, ForwardedHeadersOptions.ForwardLimit is
    /// 1, so this resolves to that proxy and everyone shares a single partition. For a
    /// household of one to three people that is the desired behaviour anyway - a shared
    /// budget of ten login attempts a minute - and per-account lockout remains the
    /// per-user defence.</item>
    /// <item>Under TestServer there is no remote address at all, hence the fallback.</item>
    /// </list>
    /// </remarks>
    private static string PartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
