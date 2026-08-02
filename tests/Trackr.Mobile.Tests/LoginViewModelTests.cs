using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Sign-in, and in particular the two-attempt 2FA flow the token endpoint requires.
/// </summary>
public sealed class LoginViewModelTests
{
    private static readonly TokenResponse AnyTokens = new(
        TokenType: "Bearer",
        AccessToken: "access",
        ExpiresIn: 3600,
        RefreshToken: "refresh");

    [Fact]
    public async Task Signing_in_stores_the_tokens_and_moves_on()
    {
        var (viewModel, api, tokenStore, navigation) = Build();

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(LoginStatus.Succeeded, AnyTokens));
        api.GetMeAsync(Arg.Any<CancellationToken>())
            .Returns(new MeResponse(Guid.NewGuid(), "owner@example.test", TwoFactorEnabled: false));

        viewModel.Email = "owner@example.test";
        viewModel.Password = "correct horse battery staple";

        await viewModel.SignInCommand.ExecuteAsync(null);

        await tokenStore.Received(1).WriteAsync(Arg.Is<StoredTokens>(t =>
            t.AccessToken == "access" && t.RefreshToken == "refresh"));

        // Fetching the account is what populates AuthSession.CurrentUser, which is what
        // raises Changed, which is what makes App swap to the signed-in shell.
        await api.Received(1).GetMeAsync(Arg.Any<CancellationToken>());

        // And the view model itself must not navigate. Crossing the auth boundary is the
        // shell swap's job; a view model doing it too would race the swap.
        Assert.Empty(navigation.ReceivedCalls());
        Assert.Null(viewModel.Error);
    }

    [Fact]
    public async Task A_two_factor_challenge_reveals_the_code_field_rather_than_showing_an_error()
    {
        // RequiresTwoFactor means the password was accepted. Presenting it as a failure
        // would tell the user they got their password wrong when they did not.
        var (viewModel, api, tokenStore, navigation) = Build();

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(LoginStatus.RequiresTwoFactor));

        viewModel.Email = "owner@example.test";
        viewModel.Password = "correct horse battery staple";

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.True(viewModel.NeedsTwoFactor);
        Assert.Null(viewModel.Error);
        await tokenStore.DidNotReceive().WriteAsync(Arg.Any<StoredTokens>());
        Assert.Empty(navigation.ReceivedCalls());
    }

    [Fact]
    public async Task The_second_attempt_resends_the_password_along_with_the_code()
    {
        // The token endpoint keeps no server-side challenge between calls - there is no
        // TwoFactorUserId cookie a native client can hold - so the second request must carry
        // the whole credential again, not just the code.
        var (viewModel, api, _, _) = Build();

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(LoginStatus.RequiresTwoFactor));

        viewModel.Email = "owner@example.test";
        viewModel.Password = "correct horse battery staple";
        await viewModel.SignInCommand.ExecuteAsync(null);

        api.ClearReceivedCalls();
        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(LoginStatus.Succeeded, AnyTokens));

        viewModel.TwoFactorCode = "123456";
        await viewModel.SignInCommand.ExecuteAsync(null);

        await api.Received(1).SignInAsync(
            Arg.Is<TokenRequest>(r =>
                r.Email == "owner@example.test"
                && r.Password == "correct horse battery staple"
                && r.TwoFactorCode == "123456"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_first_attempt_never_sends_a_code()
    {
        // A code left in the box from an earlier attempt must not ride along on a fresh
        // sign-in, where it would be stale and would spend a lockout attempt for nothing.
        var (viewModel, api, _, _) = Build();

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(LoginStatus.RequiresTwoFactor));

        viewModel.Email = "owner@example.test";
        viewModel.Password = "correct horse battery staple";
        viewModel.TwoFactorCode = "999999";

        await viewModel.SignInCommand.ExecuteAsync(null);

        await api.Received(1).SignInAsync(
            Arg.Is<TokenRequest>(r => r.TwoFactorCode == null && r.TwoFactorRecoveryCode == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_recovery_code_goes_in_the_recovery_field()
    {
        // The two are not interchangeable server-side: a TOTP code has its dashes stripped
        // and a recovery code does not, so sending one as the other fails every time.
        var (viewModel, api, _, _) = Build();

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(LoginStatus.RequiresTwoFactor));

        viewModel.Email = "owner@example.test";
        viewModel.Password = "correct horse battery staple";
        await viewModel.SignInCommand.ExecuteAsync(null);

        viewModel.UseRecoveryCode = true;
        viewModel.TwoFactorCode = "abcde-fghij";

        api.ClearReceivedCalls();
        await viewModel.SignInCommand.ExecuteAsync(null);

        await api.Received(1).SignInAsync(
            Arg.Is<TokenRequest>(r =>
                r.TwoFactorRecoveryCode == "abcde-fghij" && r.TwoFactorCode == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Switching_to_a_recovery_code_clears_a_half_typed_authenticator_code()
    {
        var (viewModel, _, _, _) = Build();

        viewModel.TwoFactorCode = "1234";
        viewModel.UseRecoveryCode = true;

        Assert.Equal("", viewModel.TwoFactorCode);
    }

    [Fact]
    public async Task A_lockout_says_when_the_account_unlocks()
    {
        var (viewModel, api, _, _) = Build();

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(
                LoginStatus.LockedOut,
                LockoutEndUtc: DateTimeOffset.UtcNow.AddMinutes(15)));

        viewModel.Email = "owner@example.test";
        viewModel.Password = "wrong";

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.NotNull(viewModel.Error);
        Assert.Contains("Too many failed attempts", viewModel.Error);
    }

    [Fact]
    public async Task An_unreachable_server_reports_its_own_message_rather_than_bad_credentials()
    {
        // CLAUDE.md section 5: a failure must reach the user as what actually happened.
        // "Wrong password" for a network outage sends people resetting a password that was
        // never the problem.
        var (viewModel, api, _, _) = Build();

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(
                LoginStatus.Failed,
                Problem: "Could not reach the server. Check your connection."));

        viewModel.Email = "owner@example.test";
        viewModel.Password = "correct horse battery staple";

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Could not reach the server. Check your connection.", viewModel.Error);
    }

    [Fact]
    public async Task Changing_server_forgets_the_old_one()
    {
        var (viewModel, _, _, navigation) = Build(out var settings);

        await viewModel.ChangeServerCommand.ExecuteAsync(null);

        await settings.Received(1).ClearAsync();
        await navigation.Received(1).GoToServerSetupAsync();
    }

    // --- helpers ----------------------------------------------------------------------

    private static (LoginViewModel ViewModel, ITrackrApiClient Api, ITokenStore TokenStore, INavigationService Navigation)
        Build() => Build(out _);

    private static (LoginViewModel ViewModel, ITrackrApiClient Api, ITokenStore TokenStore, INavigationService Navigation)
        Build(out IServerSettings settings)
    {
        var api = Substitute.For<ITrackrApiClient>();
        var tokenStore = Substitute.For<ITokenStore>();
        var navigation = Substitute.For<INavigationService>();

        settings = Substitute.For<IServerSettings>();
        settings.BaseUrl.Returns(new Uri("https://trackr.example.test/"));

        // AuthSession is a real instance rather than a substitute: the behaviour under test
        // includes it deciding to persist tokens, which is exactly what a substitute would
        // stub out.
        var session = new AuthSession(api, tokenStore, settings);

        return (new LoginViewModel(session, settings, navigation), api, tokenStore, navigation);
    }
}
