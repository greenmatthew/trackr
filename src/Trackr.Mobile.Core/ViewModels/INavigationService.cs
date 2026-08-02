namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Moving between screens, without this project needing to know what a Shell is.
/// </summary>
/// <remarks>
/// Provisional, and deliberately so: milestone 5 (CLAUDE.md section 9) settles navigation
/// properly. Named methods rather than a general <c>GoToAsync(route)</c> because there are
/// four screens and named methods can be asserted on in a test without matching strings.
/// Expect this to grow into something route-shaped once the chat and stats tabs exist.
/// </remarks>
public interface INavigationService
{
    Task GoToServerSetupAsync();

    Task GoToLoginAsync();

    Task GoToRegisterAsync();

    Task GoToHomeAsync();
}
