using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Pages;

namespace Trackr.Mobile;

public partial class App : Application
{
    private readonly AuthSession _session;

    private Window? _window;

    public App(AuthSession session)
    {
        InitializeComponent();

        _session = session;

        // The subscriber AuthSession.Changed was added for and then went without: signing in
        // or out swaps the whole shell, so no view model needs to navigate across the auth
        // boundary and none of them can get it wrong.
        _session.Changed += SyncShell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // Which shell to open depends on whether the stored token still works, and the only
        // honest way to know is to ask the server - a network call, which must not hold up
        // the window. So the window opens on a loading page that continues the splash screen
        // and is replaced as soon as the answer arrives.
        //
        // The alternative this replaces was to open a shell on whatever route it declared
        // first and navigate away a moment later, which meant every launch flickered through
        // server setup. Showing a neutral screen briefly is better than showing a wrong one.
        _window = new Window(new LoadingPage());

        _ = ResolveStartingShellAsync();

        return _window;
    }

    private async Task ResolveStartingShellAsync()
    {
        try
        {
            await _session.RestoreAsync();
        }
        finally
        {
            // RestoreAsync raises Changed on the paths that reach the server, but returns
            // early and silently when there is no server configured or no token stored -
            // both ordinary first-run states. Syncing here covers those, and is idempotent
            // when Changed has already fired.
            SyncShell();
        }
    }

    private void SyncShell() =>
        // Changed is raised from whichever thread finished the await that preceded it, and
        // Window.Page must be set on the UI thread.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_window is null)
            {
                return;
            }

            if (_session.IsSignedIn)
            {
                if (_window.Page is not AppShell)
                {
                    _window.Page = new AppShell();
                }
            }
            else if (_window.Page is not AuthShell)
            {
                _window.Page = new AuthShell(_session.HasServer);
            }
        });
}
