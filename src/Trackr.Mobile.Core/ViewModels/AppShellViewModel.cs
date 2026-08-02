using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Auth;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Backs the title bar that every signed-in tab shares: the app name and the avatar that
/// opens the profile.
/// </summary>
/// <remarks>
/// The avatar is not a tab. Three tabs is already the width the thumb wants, and the profile
/// is somewhere you go occasionally rather than one of the two surfaces the app is for -
/// logging and looking (CLAUDE.md section 1).
/// </remarks>
public sealed partial class AppShellViewModel(
    AuthSession session,
    INavigationService navigation) : ObservableObject
{
    /// <summary>Shown in the avatar circle until the account has a picture.</summary>
    public string Initials => Avatar.InitialsFrom(session.CurrentUser?.Email);

    [RelayCommand]
    private Task OpenProfileAsync() => navigation.GoToProfileAsync();
}
