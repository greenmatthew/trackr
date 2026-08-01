using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// CLAUDE.md section 8.1 - authenticator-app TOTP, confirmed by typing one working code,
/// with recovery codes issued once.
/// </summary>
public sealed class TwoFactorTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Enrolling_requires_a_working_code_and_returns_recovery_codes_once()
    {
        using var owner = await RegisterOwnerAsync();

        using var enrollResponse = await owner.PostAsync("/api/account/2fa/enroll", content: null);
        enrollResponse.EnsureSuccessStatusCode();
        var enrollment = await enrollResponse.Content.ReadFromJsonAsync<TwoFactorEnrollmentResponse>();

        Assert.StartsWith("otpauth://totp/Trackr:", enrollment!.AuthenticatorUri);
        Assert.StartsWith("data:image/svg+xml;base64,", enrollment.QrCodeSvgDataUri);

        // A wrong code must not switch 2FA on.
        using var rejected = await owner.PostAsJsonAsync(
            "/api/account/2fa/enable",
            new TwoFactorCodeRequest { Code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        var status = await owner.GetFromJsonAsync<TwoFactorStatusResponse>("/api/account/2fa");
        Assert.False(status!.IsEnabled);

        // The real code, from Identity's own TOTP provider.
        using var enabled = await owner.PostAsJsonAsync(
            "/api/account/2fa/enable",
            new TwoFactorCodeRequest { Code = await Factory.GenerateTotpAsync(OwnerEmail) });
        enabled.EnsureSuccessStatusCode();

        var codes = await enabled.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
        Assert.Equal(10, codes!.RecoveryCodes.Count);

        status = await owner.GetFromJsonAsync<TwoFactorStatusResponse>("/api/account/2fa");
        Assert.True(status!.IsEnabled);
        Assert.Equal(10, status.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Enabling_2fa_does_not_sign_the_caller_out()
    {
        // Regression test for the missing RefreshSignInAsync: SetTwoFactorEnabledAsync
        // rolls the security stamp, which would otherwise invalidate this very session.
        using var owner = await RegisterOwnerAsync();
        await EnableTwoFactorAsync(owner);

        using var me = await owner.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Login_demands_a_code_and_accepts_the_authenticator()
    {
        using var owner = await RegisterOwnerAsync();
        await EnableTwoFactorAsync(owner);

        using var client = Factory.NewClient();

        var passwordStep = await LoginBodyAsync(client, OwnerEmail, OwnerPassword);
        Assert.Equal(LoginStatus.RequiresTwoFactor, passwordStep.Status);

        // The password alone must not produce a usable session.
        using var midChallenge = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, midChallenge.StatusCode);

        using var codeStep = await client.PostAsJsonAsync(
            "/api/auth/login/2fa",
            new TwoFactorLoginRequest { Code = await Factory.GenerateTotpAsync(OwnerEmail) });
        codeStep.EnsureSuccessStatusCode();

        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Two_factor_step_without_a_password_step_reports_an_expired_challenge()
    {
        using var owner = await RegisterOwnerAsync();
        await EnableTwoFactorAsync(owner);

        using var client = Factory.NewClient();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login/2fa",
            new TwoFactorLoginRequest { Code = await Factory.GenerateTotpAsync(OwnerEmail) });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal(LoginStatus.ChallengeExpired, body!.Status);
    }

    [Fact]
    public async Task Wrong_codes_feed_the_lockout_counter()
    {
        using var owner = await RegisterOwnerAsync();
        await EnableTwoFactorAsync(owner);

        using var client = Factory.NewClient();
        await LoginAsync(client, OwnerEmail, OwnerPassword);

        LoginResponse? last = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/login/2fa",
                new TwoFactorLoginRequest { Code = "000000" });
            last = await response.Content.ReadFromJsonAsync<LoginResponse>();
        }

        Assert.Equal(LoginStatus.LockedOut, last!.Status);
    }

    [Fact]
    public async Task Recovery_code_signs_in_once_and_is_then_spent()
    {
        using var owner = await RegisterOwnerAsync();
        var recoveryCodes = await EnableTwoFactorAsync(owner);
        var code = recoveryCodes[0];

        using var first = Factory.NewClient();
        await LoginAsync(first, OwnerEmail, OwnerPassword);

        using var accepted = await first.PostAsJsonAsync(
            "/api/auth/login/recovery-code",
            new RecoveryCodeLoginRequest { RecoveryCode = code });
        accepted.EnsureSuccessStatusCode();

        using var me = await first.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var status = await first.GetFromJsonAsync<TwoFactorStatusResponse>("/api/account/2fa");
        Assert.Equal(9, status!.RecoveryCodesLeft);

        // The same code a second time must fail.
        using var second = Factory.NewClient();
        await LoginAsync(second, OwnerEmail, OwnerPassword);

        using var reused = await second.PostAsJsonAsync(
            "/api/auth/login/recovery-code",
            new RecoveryCodeLoginRequest { RecoveryCode = code });

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Disabling_2fa_requires_the_password()
    {
        using var owner = await RegisterOwnerAsync();
        await EnableTwoFactorAsync(owner);

        using var refused = await owner.PostAsJsonAsync(
            "/api/account/2fa/disable",
            new DisableTwoFactorRequest { Password = "not the password" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        using var accepted = await owner.PostAsJsonAsync(
            "/api/account/2fa/disable",
            new DisableTwoFactorRequest { Password = OwnerPassword });
        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        var status = await owner.GetFromJsonAsync<TwoFactorStatusResponse>("/api/account/2fa");
        Assert.False(status!.IsEnabled);

        // And the session survived, thanks to RefreshSignInAsync.
        using var me = await owner.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

}
