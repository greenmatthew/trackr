using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests;

public sealed class PasswordTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Changing_the_password_keeps_the_current_session_alive()
    {
        // Regression test for a missing RefreshSignInAsync. ChangePasswordAsync rolls the
        // security stamp, which is what ejects other sessions - but without a refresh it
        // would eject this one too, moments after a successful change.
        using var owner = await RegisterOwnerAsync();

        using var changed = await owner.PostAsJsonAsync(
            "/api/account/password",
            new ChangePasswordRequest
            {
                CurrentPassword = OwnerPassword,
                NewPassword = "a different long passphrase"
            });
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        using var me = await owner.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // And the new password is the one that works.
        using var fresh = Factory.NewClient();
        var withNew = await LoginBodyAsync(fresh, OwnerEmail, "a different long passphrase");
        Assert.Equal(LoginStatus.Succeeded, withNew.Status);

        using var other = Factory.NewClient();
        var withOld = await LoginBodyAsync(other, OwnerEmail, OwnerPassword);
        Assert.Equal(LoginStatus.Failed, withOld.Status);
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one()
    {
        using var owner = await RegisterOwnerAsync();

        using var response = await owner.PostAsJsonAsync(
            "/api/account/password",
            new ChangePasswordRequest
            {
                CurrentPassword = "not the current password",
                NewPassword = "a different long passphrase"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_answers_the_same_way_for_unknown_addresses()
    {
        await RegisterOwnerAsync();

        using var client = Factory.NewClient();

        using var known = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest { Email = OwnerEmail });

        using var unknown = await client.PostAsJsonAsync(
            "/api/auth/forgot-password",
            new ForgotPasswordRequest { Email = "nobody@example.test" });

        // Identical responses, so this cannot be used to find out which addresses exist.
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
    }

    [Fact]
    public async Task Reset_link_sets_a_new_password_and_works_only_once()
    {
        await RegisterOwnerAsync();

        // Stands in for the link the email sender would deliver - the endpoint receives it
        // base64url-encoded exactly like this.
        var code = await Factory.GeneratePasswordResetCodeAsync(OwnerEmail);

        using var client = Factory.NewClient();

        using var rejected = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest
            {
                Email = OwnerEmail,
                Code = "not a real code",
                NewPassword = "a different long passphrase"
            });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var accepted = await client.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest
            {
                Email = OwnerEmail,
                Code = code,
                NewPassword = "a different long passphrase"
            });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        // Resetting must not hand out a session - with the default email provider the
        // token came out of a log file.
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var loginClient = Factory.NewClient();
        var login = await LoginBodyAsync(loginClient, OwnerEmail, "a different long passphrase");
        Assert.Equal(LoginStatus.Succeeded, login.Status);

        // The same token a second time must fail: resetting rolled the security stamp the
        // token was derived from.
        using var replay = Factory.NewClient();
        using var reused = await replay.PostAsJsonAsync(
            "/api/auth/reset-password",
            new ResetPasswordRequest
            {
                Email = OwnerEmail,
                Code = code,
                NewPassword = "a third long passphrase"
            });

        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
    }
}
