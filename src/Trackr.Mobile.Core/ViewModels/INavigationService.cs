namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Moving between screens, without this project needing to know what a Shell is.
/// </summary>
/// <remarks>
/// Every method here moves <i>within</i> the signed-out shell. Crossing the auth boundary is
/// deliberately not available: signing in or out changes <c>AuthSession</c>, and App swaps the
/// whole shell in response. A view model that tried to navigate to the signed-in shell would
/// be describing a transition it does not own, and could get it wrong in a way that leaves the
/// user on a screen whose every request fails.
/// <para>
/// Named methods rather than a general <c>GoToAsync(route)</c> so that a test can assert on
/// the intent without matching strings. See <see cref="Routes"/> for the names themselves.
/// </para>
/// </remarks>
public interface INavigationService
{
    Task GoToServerSetupAsync();

    Task GoToLoginAsync();

    Task GoToRegisterAsync();

    /// <summary>
    /// Opens the profile from the avatar in the title bar. The one route that pushes rather
    /// than replaces, so it gets a back arrow and returns to the tab it was opened from.
    /// </summary>
    Task GoToProfileAsync();
}
