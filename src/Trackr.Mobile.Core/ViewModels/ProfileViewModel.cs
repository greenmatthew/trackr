using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// The account screen, reached from the avatar in the title bar.
/// </summary>
/// <remarks>
/// Holds what the milestone 3 home screen held - which account, which server, whether 2FA is
/// on, and sign out - because none of it was ever about the home screen. Home became the
/// day's totals in milestone 5 and this is where the account details belong.
/// <para>
/// Milestone 9.13 grows this into a real profile: display name, time zone, unit preferences,
/// and the account self-service that milestone 2 left out. Changing a password or enrolling
/// in 2FA stays on the web (CLAUDE.md section 10) - this screen reports the state, it does
/// not manage it.
/// </para>
/// </remarks>
public sealed partial class ProfileViewModel(
    AuthSession session,
    IServerSettings serverSettings) : ObservableObject
{
    public string Email => session.CurrentUser?.Email ?? "not signed in";

    public string Initials => Avatar.InitialsFrom(session.CurrentUser?.Email);

    public string ServerDescription => serverSettings.BaseUrl?.Host ?? "no server configured";

    public bool TwoFactorEnabled => session.CurrentUser?.TwoFactorEnabled ?? false;

    public string TwoFactorDescription =>
        TwoFactorEnabled ? "On" : "Off - enable it on the website";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        IsBusy = true;

        try
        {
            // No navigation: SignOutAsync raises AuthSession.Changed, and App swaps back to
            // the signed-out shell in response.
            await session.SignOutAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
