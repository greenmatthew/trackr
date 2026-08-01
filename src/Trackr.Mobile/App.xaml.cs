using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile;

public partial class App : Application
{
    private readonly AuthSession _session;
    private readonly INavigationService _navigation;

    public App(AuthSession session, INavigationService navigation)
    {
        InitializeComponent();

        _session = session;
        _navigation = navigation;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

        // Deciding the first screen means asking the server whether the stored token still
        // works, which is a network call and must not block the window from appearing. So
        // the shell opens on its first route and this corrects it a moment later.
        //
        // AppShell declares server-setup first precisely because it is the safe default:
        // showing setup to someone already signed in is a brief flicker, whereas opening on
        // Home before the token is verified would show a signed-out user a screen that then
        // fails every request.
        window.Created += async (_, _) => await RouteToStartingScreenAsync();

        return window;
    }

    private async Task RouteToStartingScreenAsync()
    {
        if (!_session.HasServer)
        {
            await _navigation.GoToServerSetupAsync();
            return;
        }

        var restored = await _session.RestoreAsync();

        if (restored)
        {
            await _navigation.GoToHomeAsync();
        }
        else
        {
            await _navigation.GoToLoginAsync();
        }
    }
}
