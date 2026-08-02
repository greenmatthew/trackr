using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Implements Core's navigation contract over Shell routing.
/// </summary>
/// <remarks>
/// Every route is absolute ("//name"), which resets the navigation stack rather than pushing
/// onto it - moving between server setup, login and registration should never leave a back
/// gesture that returns to a half-finished attempt.
/// <para>
/// All three routes live in AuthShell. Reaching the signed-in shell is not navigation at all
/// but a swap of Window.Page, driven by AuthSession.Changed in App - see
/// <see cref="INavigationService"/>.
/// </para>
/// </remarks>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToServerSetupAsync() => GoToAsync($"//{Routes.ServerSetup}");

    public Task GoToLoginAsync() => GoToAsync($"//{Routes.Login}");

    public Task GoToRegisterAsync() => GoToAsync($"//{Routes.Register}");

    // Relative, unlike every other route here: pushing onto the current tab's stack is what
    // gives the profile a back arrow and returns the user to the tab they opened it from.
    public Task GoToProfileAsync() => GoToAsync(Routes.Profile);

    private static Task GoToAsync(string route) =>
        // Shell navigation must happen on the UI thread, and commands may complete on a
        // thread-pool thread after an await.
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));
}
