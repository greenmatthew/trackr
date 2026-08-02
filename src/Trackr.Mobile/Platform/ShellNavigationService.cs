using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Implements Core's navigation contract over Shell routing.
/// </summary>
/// <remarks>
/// Provisional along with <see cref="INavigationService"/> itself - milestone 5 settles how
/// navigation actually works once there are chat and stats tabs to move between.
/// <para>
/// Every route is absolute ("//name"), which resets the navigation stack rather than pushing
/// onto it. That is what these transitions want: after signing in there must be no
/// back gesture that returns to the login screen, and after signing out none that returns to
/// the signed-in one.
/// </para>
/// </remarks>
public sealed class ShellNavigationService : INavigationService
{
    public Task GoToServerSetupAsync() => GoToAsync($"//{Routes.ServerSetup}");

    public Task GoToLoginAsync() => GoToAsync($"//{Routes.Login}");

    public Task GoToRegisterAsync() => GoToAsync($"//{Routes.Register}");

    public Task GoToHomeAsync() => GoToAsync($"//{Routes.Home}");

    private static Task GoToAsync(string route) =>
        // Shell navigation must happen on the UI thread, and commands may complete on a
        // thread-pool thread after an await.
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(route));
}

/// <summary>Route names, shared between AppShell.xaml and the navigation service.</summary>
public static class Routes
{
    public const string ServerSetup = "server-setup";
    public const string Login = "login";
    public const string Register = "register";
    public const string Home = "home";
}
