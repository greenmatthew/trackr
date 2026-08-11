using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using AndroidX.Core.Content;
using AndroidX.Core.View;
using Google.Android.Material.AppBar;

namespace Trackr.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    /// <summary>
    /// Re-themes the parts of the window Android does not, when the system switches between light
    /// and dark: the strip behind the title bar, and the system bars' own icons.
    /// </summary>
    /// <remarks>
    /// <c>ConfigurationChanges</c> above lists <see cref="ConfigChanges.UiMode"/>, so Android hands
    /// the change to this activity instead of recreating it - which is what keeps the navigation
    /// stack and any half-typed text alive across a theme switch, and is why it is listed. The cost
    /// is that already-inflated views keep the drawables they resolved at inflation time. Everything
    /// MAUI draws re-themes itself through <c>AppThemeBinding</c>, but the <c>AppBarLayout</c> takes
    /// its background from an Android theme overlay (Resources/values/styles.xml) and does not, so a
    /// light-to-dark switch would otherwise leave a white band above a navy title bar until the next
    /// cold start.
    /// </remarks>
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);

        // The system bars draw their clock and icons over the app's own background, so which of
        // dark or light they use is the app's to declare. Android reads it from the theme's
        // `windowLightStatusBar` at inflation and, like the background below, does not revisit it -
        // leaving a dark clock on the navy bar after a switch into dark. Both bars, because the
        // gesture pill has the same problem at the other end of the screen.
        var isDark = (newConfig.UiMode & UiMode.NightMask) == UiMode.NightYes;

        if (Window is { DecorView: { } decorView } window)
        {
            var systemBars = WindowCompat.GetInsetsController(window, decorView);

            systemBars.AppearanceLightStatusBars = !isDark;
            systemBars.AppearanceLightNavigationBars = !isDark;
        }

        // Null whenever no Shell is on screen yet - during startup, or between the two shells being
        // swapped on sign-in. Both cases inflate a fresh AppBarLayout afterwards, which reads the
        // colour for whatever configuration is current by then.
        if (FindAppBar(Window?.DecorView) is not { } appBar)
        {
            return;
        }

        // ContextCompat rather than Resources.GetColor: the theme-aware overload of the latter wants
        // API 23 and this app declares 21, so calling it directly is a CA1416 warning about a device
        // nobody has.
        appBar.SetBackgroundColor(
            new Android.Graphics.Color(ContextCompat.GetColor(this, Resource.Color.titleBarBackground)));
    }

    /// <summary>
    /// The Shell's <see cref="AppBarLayout"/>, found by walking the view tree.
    /// </summary>
    /// <remarks>
    /// By type rather than by <c>FindViewById(Resource.Id.navigationlayout_appbar)</c>, which is the
    /// id MAUI's own layout gives it and which returns null here at runtime - the id constant the app
    /// compiles against does not resolve to the one the view was inflated with. Searching for the
    /// type is both shorter and immune to that, since MAUI's Shell puts exactly one on screen.
    /// </remarks>
    private static AppBarLayout? FindAppBar(Android.Views.View? view) => view switch
    {
        null => null,
        AppBarLayout appBar => appBar,
        Android.Views.ViewGroup group => Enumerable
            .Range(0, group.ChildCount)
            .Select(index => FindAppBar(group.GetChildAt(index)))
            .FirstOrDefault(found => found is not null),
        _ => null
    };
}
