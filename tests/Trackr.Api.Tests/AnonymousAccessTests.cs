using System.Net;
using Trackr.Api.Tests.Infrastructure;
using Xunit;

namespace Trackr.Api.Tests;

public sealed class AnonymousAccessTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    /// <remarks>
    /// The stack-breaker regression test. Program.cs applies a fallback policy requiring a
    /// signed-in user, and the health routes opt out. If that opt-out is ever lost, the
    /// container HEALTHCHECK starts failing, `frontend` never satisfies its
    /// `depends_on: service_healthy`, and the whole compose stack stops coming up.
    /// </remarks>
    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/health/live")]
    [InlineData("/api/health/ready")]
    public async Task Health_endpoints_stay_anonymous(string route)
    {
        using var client = Factory.NewClient();

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <remarks>
    /// The "no 302 to /Account/Login" regression test. Cookie authentication redirects by
    /// default, which would turn a fetch() in the Blazor client into a 200 plus a page of
    /// HTML instead of a status code it can act on.
    /// </remarks>
    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/invites")]
    [InlineData("/api/account/2fa")]
    [InlineData("/api/nutrients")]
    [InlineData("/api/foods")]
    [InlineData("/api/log")]
    // By id, because there is no GET /api/images collection route - asking for one would be
    // answered 405 by routing before authentication ever ran, which would prove nothing.
    [InlineData("/api/images/0198c0de-0000-7000-8000-000000000000")]
    public async Task Protected_endpoints_answer_a_bare_401(string route)
    {
        using var client = Factory.NewClient();

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Creating_an_invite_requires_a_session()
    {
        using var client = Factory.NewClient();

        using var response = await client.PostAsync("/api/invites", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
