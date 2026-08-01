using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The bearer-token scheme the Android app signs in with, alongside the web app's cookie.
/// </summary>
/// <remarks>
/// The behaviour worth pinning down is that the two schemes are genuinely equivalent from
/// the API's point of view - same lockout, same 2FA requirement, same access to protected
/// endpoints - and that the fallback authorization policy accepts both. The last part is
/// easy to get wrong in a way that only shows up on a real device.
/// </remarks>
public sealed class TokenAuthTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Token_sign_in_returns_a_bearer_token_that_reaches_a_protected_endpoint()
    {
        using var owner = await RegisterOwnerAsync();

        using var client = Factory.NewClient();
        var tokens = await GetTokensAsync(client, OwnerEmail, OwnerPassword);

        Assert.Equal("Bearer", tokens.TokenType);
        Assert.NotEmpty(tokens.AccessToken);
        Assert.NotEmpty(tokens.RefreshToken);
        Assert.True(tokens.ExpiresIn > 0);

        // A fresh client, so nothing is riding on a cookie left over from the sign-in.
        using var bearer = Factory.NewClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var me = await bearer.GetFromJsonAsync<MeResponse>("/api/auth/me");

        Assert.Equal(OwnerEmail, me!.Email);
    }

    [Fact]
    public async Task Protected_endpoints_reject_a_caller_with_no_token()
    {
        // The fallback policy names both schemes. If it ever named only the bearer scheme,
        // this would still pass while every cookie session broke - hence the cookie half of
        // the pair being asserted in AnonymousAccessTests rather than here.
        await RegisterOwnerAsync();

        using var client = Factory.NewClient();
        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_garbled_token_is_rejected_rather_than_throwing()
    {
        await RegisterOwnerAsync();

        using var client = Factory.NewClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        using var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_reports_failure_without_revealing_whether_the_account_exists()
    {
        await RegisterOwnerAsync();

        using var client = Factory.NewClient();

        var wrongPassword = await TokenFailureAsync(client, OwnerEmail, "not the right password at all");
        var unknownAccount = await TokenFailureAsync(client, "nobody@example.test", OwnerPassword);

        Assert.Equal(LoginStatus.Failed, wrongPassword.Status);
        Assert.Equal(LoginStatus.Failed, unknownAccount.Status);
    }

    [Fact]
    public async Task Refresh_exchanges_a_refresh_token_for_a_working_access_token()
    {
        using var owner = await RegisterOwnerAsync();

        using var client = Factory.NewClient();
        var original = await GetTokensAsync(client, OwnerEmail, OwnerPassword);

        using var refreshResponse = await client.PostAsJsonAsync(
            "/api/auth/token/refresh",
            new RefreshRequest { RefreshToken = original.RefreshToken });
        refreshResponse.EnsureSuccessStatusCode();

        var refreshed = await refreshResponse.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(refreshed);

        using var bearer = Factory.NewClient();
        bearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed!.AccessToken);

        using var me = await bearer.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task A_nonsense_refresh_token_is_rejected()
    {
        await RegisterOwnerAsync();

        using var client = Factory.NewClient();
        using var response = await client.PostAsJsonAsync(
            "/api/auth/token/refresh",
            new RefreshRequest { RefreshToken = "definitely not protected by the key ring" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_invalidates_an_outstanding_refresh_token()
    {
        // This is what ValidateSecurityStampAsync buys: a refresh token must not outlive the
        // password it was obtained with, or a stolen phone stays signed in for 30 days after
        // the owner reacts to it.
        using var owner = await RegisterOwnerAsync();

        using var client = Factory.NewClient();
        var tokens = await GetTokensAsync(client, OwnerEmail, OwnerPassword);

        using var changed = await owner.PostAsJsonAsync(
            "/api/account/password",
            new ChangePasswordRequest
            {
                CurrentPassword = OwnerPassword,
                NewPassword = "an entirely different passphrase"
            });
        changed.EnsureSuccessStatusCode();

        using var response = await client.PostAsJsonAsync(
            "/api/auth/token/refresh",
            new RefreshRequest { RefreshToken = tokens.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Two_factor_accounts_must_supply_a_code_in_the_same_request()
    {
        using var owner = await RegisterOwnerAsync();
        await EnableTwoFactorAsync(owner);

        using var client = Factory.NewClient();

        // First attempt: correct password, no code.
        var challenged = await TokenFailureAsync(client, OwnerEmail, OwnerPassword);
        Assert.Equal(LoginStatus.RequiresTwoFactor, challenged.Status);

        // Second attempt: the whole request again, this time with the code. Unlike the web
        // flow there is no server-side challenge in between, so the password is re-checked.
        using var response = await client.PostAsJsonAsync(
            "/api/auth/token",
            new TokenRequest
            {
                Email = OwnerEmail,
                Password = OwnerPassword,
                TwoFactorCode = await Factory.GenerateTotpAsync(OwnerEmail)
            });
        response.EnsureSuccessStatusCode();

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>();
        Assert.NotNull(tokens);
        Assert.NotEmpty(tokens!.AccessToken);
    }

    [Fact]
    public async Task A_recovery_code_also_works_and_is_then_spent()
    {
        using var owner = await RegisterOwnerAsync();
        var recoveryCodes = await EnableTwoFactorAsync(owner);

        using var client = Factory.NewClient();

        using var accepted = await client.PostAsJsonAsync(
            "/api/auth/token",
            new TokenRequest
            {
                Email = OwnerEmail,
                Password = OwnerPassword,
                TwoFactorRecoveryCode = recoveryCodes[0]
            });
        accepted.EnsureSuccessStatusCode();

        // The same code a second time must not work.
        using var reused = await client.PostAsJsonAsync(
            "/api/auth/token",
            new TokenRequest
            {
                Email = OwnerEmail,
                Password = OwnerPassword,
                TwoFactorRecoveryCode = recoveryCodes[0]
            });

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Wrong_passwords_lock_the_account_out_over_the_token_path()
    {
        await RegisterOwnerAsync();

        using var client = Factory.NewClient();

        TokenLoginResponse? last = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            last = await TokenFailureAsync(client, OwnerEmail, "wrong password");
        }

        Assert.Equal(LoginStatus.LockedOut, last!.Status);
        Assert.NotNull(last.LockoutEndUtc);

        // And the correct password does not get in while the lockout stands.
        var duringLockout = await TokenFailureAsync(client, OwnerEmail, OwnerPassword);
        Assert.Equal(LoginStatus.LockedOut, duringLockout.Status);
    }

    [Fact]
    public async Task Wrong_two_factor_codes_also_feed_the_lockout_counter()
    {
        // TokenAsync verifies the code by hand rather than through
        // TwoFactorAuthenticatorSignInAsync, so the lockout bookkeeping it would otherwise
        // inherit has to be done explicitly. This is the test that it actually was.
        using var owner = await RegisterOwnerAsync();
        await EnableTwoFactorAsync(owner);

        using var client = Factory.NewClient();

        TokenLoginResponse? last = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await client.PostAsJsonAsync(
                "/api/auth/token",
                new TokenRequest
                {
                    Email = OwnerEmail,
                    Password = OwnerPassword,
                    TwoFactorCode = "000000"
                });

            last = await response.Content.ReadFromJsonAsync<TokenLoginResponse>();
        }

        Assert.Equal(LoginStatus.LockedOut, last!.Status);
    }

    // --- helpers ----------------------------------------------------------------------

    private static async Task<TokenResponse> GetTokensAsync(
        HttpClient client,
        string email,
        string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/token",
            new TokenRequest { Email = email, Password = password });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("The token endpoint returned no body.");
    }

    private static async Task<TokenLoginResponse> TokenFailureAsync(
        HttpClient client,
        string email,
        string password)
    {
        using var response = await client.PostAsJsonAsync(
            "/api/auth/token",
            new TokenRequest { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        return await response.Content.ReadFromJsonAsync<TokenLoginResponse>()
            ?? throw new InvalidOperationException("The token endpoint returned no body.");
    }
}
