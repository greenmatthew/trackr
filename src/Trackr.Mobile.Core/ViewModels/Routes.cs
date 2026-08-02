namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// The one place a route name is written down.
/// </summary>
/// <remarks>
/// In Core rather than beside the Shell that consumes it, for two reasons: the navigation
/// service is an implementation of a Core contract and should not own the vocabulary that
/// contract is expressed in, and the test project references Core only - so a route name
/// living in the MAUI project could not be asserted on without an Android SDK.
/// <para>
/// AppShell.xaml binds these with <c>{x:Static}</c> rather than repeating the strings.
/// That was previously a hand-maintained pair of literals, and the failure mode was poor:
/// renaming one side compiled cleanly and threw at runtime inside a fire-and-forget
/// handler, which surfaces as a button that does nothing rather than as a crash.
/// </para>
/// </remarks>
public static class Routes
{
    public const string ServerSetup = "server-setup";

    public const string Login = "login";

    public const string Register = "register";

    public const string Home = "home";
}
