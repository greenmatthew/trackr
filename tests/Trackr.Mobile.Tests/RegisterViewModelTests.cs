using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Creating an account from the phone: the two registration modes, and what happens when the
/// server says no.
/// </summary>
public sealed class RegisterViewModelTests
{
    // --- which mode the form is in ----------------------------------------------------

    [Fact]
    public async Task Bootstrap_mode_asks_for_no_invite()
    {
        var (viewModel, api, _, _) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.Bootstrap);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBootstrap);
        Assert.False(viewModel.NeedsInvite);
    }

    [Fact]
    public async Task Invite_mode_asks_for_an_invite()
    {
        var (viewModel, api, _, _) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.InviteRequired);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.NeedsInvite);
        Assert.False(viewModel.IsBootstrap);
    }

    /// <summary>
    /// A server that cannot be asked is assumed closed. Guessing the other way would show a
    /// form that cannot succeed, and imply the server was unclaimed when it may not be.
    /// </summary>
    [Fact]
    public async Task Assumes_an_invite_is_needed_when_the_server_cannot_be_asked()
    {
        var (viewModel, api, _, _) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs((RegistrationMode?)null);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.NeedsInvite);
    }

    [Fact]
    public async Task Cannot_submit_an_invite_form_without_an_invite()
    {
        var (viewModel, api, _, _) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.InviteRequired);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.Email = "member@example.test";
        viewModel.Password = "correct horse battery staple";

        Assert.False(viewModel.RegisterCommand.CanExecute(null));

        viewModel.InviteToken = "abc123";

        Assert.True(viewModel.RegisterCommand.CanExecute(null));
    }

    // --- the invite token -------------------------------------------------------------

    [Theory]
    // A bare token, which is what someone typing it by hand produces.
    [InlineData("abc123", "abc123")]
    [InlineData("  abc123  ", "abc123")]
    // The whole invite link, which is what the clipboard actually holds.
    [InlineData("https://trackr.example.test/register?token=abc123", "abc123")]
    [InlineData("https://trackr.example.test/register?token=abc123&x=1", "abc123")]
    [InlineData("http://localhost:8000/register?x=1&token=abc123", "abc123")]
    // Percent-encoding survives the round trip.
    [InlineData("https://trackr.example.test/register?token=a%2Bb", "a+b")]
    public void Finds_the_token_in_whatever_was_pasted(string input, string expected)
    {
        Assert.True(RegisterViewModel.TryExtractInviteToken(input, out var token));
        Assert.Equal(expected, token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // A link with no token is a mis-paste, not a token that looks like a URL. Treating it as
    // one would send the server a whole URL as the invite and produce a baffling rejection.
    [InlineData("https://trackr.example.test/register")]
    [InlineData("https://trackr.example.test/register?token=")]
    public void Rejects_input_with_no_token_in_it(string input)
    {
        Assert.False(RegisterViewModel.TryExtractInviteToken(input, out _));
    }

    // --- registering ------------------------------------------------------------------

    [Fact]
    public async Task Registers_then_signs_in_and_leaves_the_shell_swap_to_take_over()
    {
        var (viewModel, api, tokenStore, navigation) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.Bootstrap);
        api.RegisterAsync(null!).ReturnsForAnyArgs(RegisterResult.Ok);
        api.SignInAsync(null!).ReturnsForAnyArgs(new SignInResult(
            LoginStatus.Succeeded,
            new TokenResponse("Bearer", "access", 3600, "refresh")));
        api.GetMeAsync().ReturnsForAnyArgs(new MeResponse(Guid.NewGuid(), "owner@example.test", false));

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Email = "owner@example.test";
        viewModel.Password = "correct horse battery staple";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Error);
        await api.Received(1).RegisterAsync(Arg.Any<RegisterRequest>(), Arg.Any<CancellationToken>());
        // The token is what makes the account usable; registering alone does not issue one.
        await tokenStore.Received(1).WriteAsync(Arg.Any<StoredTokens>());

        // Signing in raised AuthSession.Changed, and App swaps the shell on that. The view
        // model must not also navigate - it would race the swap it does not own.
        Assert.Empty(navigation.ReceivedCalls());
    }

    [Fact]
    public async Task Sends_the_extracted_token_not_the_whole_link()
    {
        var (viewModel, api, _, _) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.InviteRequired);
        api.RegisterAsync(null!).ReturnsForAnyArgs(RegisterResult.Failed("nope"));

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Email = "member@example.test";
        viewModel.Password = "correct horse battery staple";
        viewModel.InviteToken = "https://trackr.example.test/register?token=abc123";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        await api.Received(1).RegisterAsync(
            Arg.Is<RegisterRequest>(r => r.InviteToken == "abc123"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Trims_the_email_before_sending_it()
    {
        var (viewModel, api, _, _) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.Bootstrap);
        api.RegisterAsync(null!).ReturnsForAnyArgs(RegisterResult.Failed("nope"));

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Email = "  owner@example.test  ";
        viewModel.Password = "correct horse battery staple";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        await api.Received(1).RegisterAsync(
            Arg.Is<RegisterRequest>(r => r.Email == "owner@example.test"), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The server's own wording is better than anything this app could infer - "passwords must
    /// be at least 12 characters" versus "that did not work".
    /// </summary>
    [Fact]
    public async Task Shows_what_the_server_objected_to()
    {
        var (viewModel, api, _, navigation) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.Bootstrap);
        api.RegisterAsync(null!).ReturnsForAnyArgs(
            RegisterResult.Failed("Passwords must be at least 12 characters."));

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Email = "owner@example.test";
        viewModel.Password = "short";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Equal("Passwords must be at least 12 characters.", viewModel.Error);
        Assert.Empty(navigation.ReceivedCalls());
    }

    [Fact]
    public async Task Does_not_sign_in_when_registration_failed()
    {
        var (viewModel, api, _, _) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.InviteRequired);
        api.RegisterAsync(null!).ReturnsForAnyArgs(RegisterResult.Failed("That invite is not valid."));

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Email = "member@example.test";
        viewModel.Password = "correct horse battery staple";
        viewModel.InviteToken = "spent";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        await api.DidNotReceiveWithAnyArgs().SignInAsync(null!);
    }

    /// <summary>
    /// The awkward middle state, and the reason it gets its own test: the account exists and
    /// an invite has been spent, but no token was issued. Saying "that did not work" here
    /// invites the obvious next move - register again - which burns a second invite and then
    /// fails on a duplicate email.
    /// </summary>
    [Fact]
    public async Task Says_the_account_was_created_when_only_the_sign_in_failed()
    {
        var (viewModel, api, _, navigation) = Build();
        api.GetRegistrationModeAsync().ReturnsForAnyArgs(RegistrationMode.InviteRequired);
        api.RegisterAsync(null!).ReturnsForAnyArgs(RegisterResult.Ok);
        api.SignInAsync(null!).ReturnsForAnyArgs(new SignInResult(
            LoginStatus.Failed,
            Problem: "Could not reach the server."));

        await viewModel.LoadCommand.ExecuteAsync(null);
        viewModel.Email = "member@example.test";
        viewModel.Password = "correct horse battery staple";
        viewModel.InviteToken = "abc123";

        await viewModel.RegisterCommand.ExecuteAsync(null);

        Assert.Contains("was created", viewModel.Error);
        Assert.Contains("not register again", viewModel.Error);
        // Login, not the signed-in shell: there is no session to swap to, and the whole point
        // of this state is that the user must sign in by hand.
        await navigation.Received(1).GoToLoginAsync();
        Assert.Single(navigation.ReceivedCalls());
    }

    [Fact]
    public async Task Can_go_back_to_sign_in()
    {
        var (viewModel, _, _, navigation) = Build();

        await viewModel.BackToLoginCommand.ExecuteAsync(null);

        await navigation.Received(1).GoToLoginAsync();
    }

    // --- helpers ----------------------------------------------------------------------

    private static (RegisterViewModel ViewModel, ITrackrApiClient Api, ITokenStore TokenStore, INavigationService Navigation)
        Build()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var tokenStore = Substitute.For<ITokenStore>();
        var navigation = Substitute.For<INavigationService>();

        var settings = Substitute.For<IServerSettings>();
        settings.BaseUrl.Returns(new Uri("https://trackr.example.test/"));

        // A real AuthSession, matching LoginViewModelTests: persisting the token on success is
        // part of the behaviour under test, and a substitute would stub exactly that out.
        var session = new AuthSession(api, tokenStore, settings);

        return (new RegisterViewModel(api, session, navigation), api, tokenStore, navigation);
    }
}
