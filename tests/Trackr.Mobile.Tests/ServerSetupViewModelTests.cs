using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Tests;

/// <summary>
/// First-run setup: turning what someone types into a server the app can reach.
/// </summary>
public sealed class ServerSetupViewModelTests
{
    [Theory]
    // The common case: a bare hostname, which must become https and gain a trailing slash.
    [InlineData("trackr.example.com", "https://trackr.example.com/")]
    [InlineData("  trackr.example.com  ", "https://trackr.example.com/")]
    [InlineData("https://trackr.example.com", "https://trackr.example.com/")]
    [InlineData("https://trackr.example.com/", "https://trackr.example.com/")]
    // http is allowed, but only when asked for explicitly - never inferred.
    [InlineData("http://192.168.1.10:8000", "http://192.168.1.10:8000/")]
    // A sub-path install. The trailing slash is what stops new Uri(base, "api/...") from
    // discarding the "/trackr" segment.
    [InlineData("https://home.example.com/trackr", "https://home.example.com/trackr/")]
    // A bare host and port. The colon here is a port, not a scheme, even though Uri.TryCreate
    // will happily read "localhost" as one.
    [InlineData("localhost:8000", "https://localhost:8000/")]
    [InlineData("192.168.1.10:8000", "https://192.168.1.10:8000/")]
    public void Normalises_what_a_person_would_type(string input, string expected)
    {
        Assert.True(ServerSetupViewModel.TryNormalise(input, out var result));
        Assert.Equal(expected, result.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    // Schemes that are syntactically valid URIs but cannot host an API.
    [InlineData("ftp://example.com")]
    // Uses "scheme:path", not "scheme://", so a naive "://" check misses it - and prefixing
    // https produces a URL that parses fine with "mailto:someone" as userinfo.
    [InlineData("mailto:someone@example.com")]
    [InlineData("file:///etc/passwd")]
    public void Rejects_input_that_cannot_be_a_server(string input)
    {
        Assert.False(ServerSetupViewModel.TryNormalise(input, out _));
    }

    [Fact]
    public async Task Saves_the_address_and_moves_on_when_the_server_answers()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var settings = Substitute.For<IServerSettings>();
        var navigation = Substitute.For<INavigationService>();

        api.CheckServerAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(ServerCheckResult.Reachable);

        var viewModel = new ServerSetupViewModel(api, settings, navigation)
        {
            Address = "trackr.example.com"
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        await settings.Received(1).SetBaseUrlAsync(new Uri("https://trackr.example.com/"));
        await navigation.Received(1).GoToLoginAsync();
        Assert.Null(viewModel.Error);
    }

    [Fact]
    public async Task Does_not_save_an_address_the_server_never_answered_on()
    {
        // The point of probing at all: saving an unreachable address would strand the app on
        // a login screen that can never succeed, with no obvious way back.
        var api = Substitute.For<ITrackrApiClient>();
        var settings = Substitute.For<IServerSettings>();
        var navigation = Substitute.For<INavigationService>();

        api.CheckServerAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
            .Returns(ServerCheckResult.Failed("Could not reach that address."));

        var viewModel = new ServerSetupViewModel(api, settings, navigation)
        {
            Address = "trackr.example.com"
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        await settings.DidNotReceive().SetBaseUrlAsync(Arg.Any<Uri>());
        await navigation.DidNotReceive().GoToLoginAsync();
        Assert.Equal("Could not reach that address.", viewModel.Error);
    }

    [Fact]
    public async Task Reports_a_malformed_address_without_calling_the_server()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var settings = Substitute.For<IServerSettings>();
        var navigation = Substitute.For<INavigationService>();

        var viewModel = new ServerSetupViewModel(api, settings, navigation)
        {
            Address = "ftp://example.com"
        };

        await viewModel.ConnectCommand.ExecuteAsync(null);

        await api.DidNotReceive().CheckServerAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>());
        Assert.NotNull(viewModel.Error);
    }

    [Fact]
    public void Cannot_connect_until_something_is_typed()
    {
        var viewModel = new ServerSetupViewModel(
            Substitute.For<ITrackrApiClient>(),
            Substitute.For<IServerSettings>(),
            Substitute.For<INavigationService>());

        Assert.False(viewModel.ConnectCommand.CanExecute(null));

        viewModel.Address = "trackr.example.com";

        // Relies on [NotifyCanExecuteChangedFor] having re-evaluated the guard; without that
        // attribute the button stays greyed out until something else forces a refresh.
        Assert.True(viewModel.ConnectCommand.CanExecute(null));
    }
}
