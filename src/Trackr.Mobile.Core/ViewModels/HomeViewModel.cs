using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// The placeholder signed-in screen for milestone 3.
/// </summary>
/// <remarks>
/// Its whole job is to prove the slice end to end: the token in Android's keystore reached a
/// protected endpoint and came back with a real account. Milestone 8 replaces this with the
/// chat, and milestone 10 adds the stats tab beside it.
/// </remarks>
public sealed partial class HomeViewModel(
    AuthSession session,
    IServerSettings serverSettings) : ObservableObject
{
    public string Email => session.CurrentUser?.Email ?? "not signed in";

    public string ServerDescription => serverSettings.BaseUrl?.Host ?? "no server configured";

    public bool TwoFactorEnabled => session.CurrentUser?.TwoFactorEnabled ?? false;

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
