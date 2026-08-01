using System.Net;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// CLAUDE.md section 8.2 - lockout is the primary defence against password guessing.
/// </summary>
public sealed class LockoutTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        await RegisterOwnerAsync();

        using var attacker = Factory.NewClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var body = await LoginBodyAsync(attacker, OwnerEmail, "definitely wrong");

            // The fifth failure is the one that trips it.
            var expected = attempt < 5 ? LoginStatus.Failed : LoginStatus.LockedOut;
            Assert.Equal(expected, body.Status);
        }

        // The point of the test: knowing the password no longer helps.
        var afterLockout = await LoginBodyAsync(attacker, OwnerEmail, OwnerPassword);

        Assert.Equal(LoginStatus.LockedOut, afterLockout.Status);
        Assert.NotNull(afterLockout.LockoutEndUtc);
        Assert.True(afterLockout.LockoutEndUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Unknown_account_reports_the_same_failure_as_a_wrong_password()
    {
        await RegisterOwnerAsync();

        using var client = Factory.NewClient();

        using var response = await LoginAsync(client, "nobody@example.test", "whatever at all");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await LoginBodyAsync(client, "nobody@example.test", "whatever at all");

        // Not LockedOut and not a distinct "no such user" - the endpoint must not reveal
        // which addresses have accounts.
        Assert.Equal(LoginStatus.Failed, body.Status);
    }
}
