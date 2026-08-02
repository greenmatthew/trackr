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
/// The shells bind these with <c>{x:Static}</c> rather than repeating the strings. That was
/// previously a hand-maintained pair of literals, and the failure mode was poor: renaming one
/// side compiled cleanly and threw at runtime inside a fire-and-forget handler, which
/// surfaces as a button that does nothing rather than as a crash.
/// </para>
/// </remarks>
public static class Routes
{
    // --- AuthShell -----------------------------------------------------------------------

    public const string ServerSetup = "server-setup";

    public const string Login = "login";

    public const string Register = "register";

    // --- AppShell ------------------------------------------------------------------------

    /// <summary>Today's totals. The glanceable surface, and the tab the app opens on.</summary>
    public const string Home = "home";

    /// <summary>Where food is logged. The input surface - see CLAUDE.md section 1.</summary>
    public const string Chat = "chat";

    /// <summary>Week and month summaries.</summary>
    public const string Trends = "trends";

    /// <summary>
    /// Reached by the avatar in the title bar rather than by a tab, and pushed rather than
    /// switched to, so it gets a back arrow. Registered with <c>Routing.RegisterRoute</c> in
    /// AppShell's constructor instead of being declared as ShellContent, because a route
    /// inside the TabBar would put it in the tab strip.
    /// </summary>
    public const string Profile = "profile";
}
