using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// CLAUDE.md section 8.3 - rate limiting on the auth endpoints, the second line behind
/// lockout. Lockout protects one account; this covers attempts spread across many.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RateLimitTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Login_starts_rejecting_past_the_configured_limit()
    {
        // Its own factory with a deliberately tiny limit, rather than making 1000 requests.
        await using var factory = new TrackrApiFactory(
            postgres.ConnectionString,
            new Dictionary<string, string> { ["Trackr:RateLimiting:LoginPermitLimit"] = "2" });

        await factory.ResetDatabaseAsync();

        using var client = factory.NewClient();

        // Two permitted attempts. Wrong credentials are fine - the limiter runs before
        // authentication either way.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            using var allowed = await client.PostAsJsonAsync(
                "/api/auth/login",
                new LoginRequest { Email = "nobody@example.test", Password = "whatever at all" });

            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        using var rejected = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = "nobody@example.test", Password = "whatever at all" });

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // The caller is told when to come back rather than being left to guess.
        Assert.NotNull(rejected.Headers.RetryAfter);
    }
}
