using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Sync;
using Trackr.Mobile.Pages;

namespace Trackr.Mobile;

public partial class App : Application
{
    private readonly AuthSession _session;
    private readonly IServiceProvider _services;

    private Window? _window;

    public App(AuthSession session, IServiceProvider services)
    {
        InitializeComponent();

        _session = session;

        // Both shells have constructor dependencies - AppShell a view model for its title
        // bar, AuthShell the session it reads HasServer from - and each swap needs a fresh
        // one, so they are resolved on demand rather than injected once.
        _services = services;

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

        // Coming back to the app is the most likely moment for a queued meal to become sendable:
        // the phone has usually moved, or has been put down somewhere with wifi, since it was last
        // looked at. OutboxSync also listens to connectivity itself, which covers the app being
        // open when the signal returns; this covers the far commoner case of it not being.
        _window.Resumed += (_, _) => FlushQueue();

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

    /// <summary>
    /// Empties the offline queue, without holding up whatever prompted it.
    /// </summary>
    /// <remarks>
    /// Nothing awaits this and nothing shows a spinner for it: a delivery the user did not ask for
    /// should not be able to make the app feel slow. Home reads the queue every time it appears -
    /// and flushes it again itself - so what this changes is on screen the next time anybody looks.
    /// </remarks>
    private void FlushQueue() => _ = _services.GetRequiredService<OutboxSync>().FlushAsync();

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
                    _window.Page = _services.GetRequiredService<AppShell>();
                }

                // Signing back in is the other moment a stuck queue can move: rows survive a
                // sign-out precisely so a meal is not lost by one, and a session that ended while
                // they waited is the reason several of them may have stalled.
                FlushQueue();
            }
            else if (_window.Page is not AuthShell)
            {
                _window.Page = _services.GetRequiredService<AuthShell>();
            }
        });
}
