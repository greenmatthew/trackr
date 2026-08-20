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
public sealed partial class AppShellViewModel : ObservableObject
{
    private readonly AuthSession _session;
    private readonly AvatarStore _avatars;
    private readonly INavigationService _navigation;

    public AppShellViewModel(AuthSession session, AvatarStore avatars, INavigationService navigation)
    {
        _session = session;
        _avatars = avatars;
        _navigation = navigation;

        // The picture is changed on the profile screen and drawn here, so this hears about it
        // from the store rather than from the screen that did it. Never unsubscribed: one of
        // these exists per signed-in shell, and a shell is only replaced by signing out - at
        // which point the app is showing login and holding one dead handler.
        _avatars.Changed += OnAvatarChanged;

        // And to the account, so a display name saved on the profile screen reaches the initials
        // here rather than waiting for the next launch.
        _session.AccountChanged += OnAccountChanged;
    }

    /// <summary>The picture, or null when the initials are shown instead.</summary>
    public byte[]? Picture => _avatars.Content;

    public bool HasPicture => _avatars.HasPicture;

    /// <summary>Shown in the avatar circle until the account has a picture.</summary>
    /// <remarks>
    /// The display name where there is one, and the email address otherwise - which is the point of
    /// having a name on a server a household shares.
    /// </remarks>
    public string Initials =>
        Avatar.InitialsFrom(_session.CurrentUser?.DisplayName ?? _session.CurrentUser?.Email);

    /// <summary>
    /// Brings the held picture up to date. Driven by the shell appearing rather than by the
    /// constructor, so a network fetch never sits between signing in and the first frame.
    /// </summary>
    [RelayCommand]
    private Task LoadAvatarAsync() => _avatars.EnsureLoadedAsync();

    [RelayCommand]
    private Task OpenProfileAsync() => _navigation.GoToProfileAsync();

    private void OnAccountChanged() => OnPropertyChanged(nameof(Initials));

    private void OnAvatarChanged()
    {
        OnPropertyChanged(nameof(Picture));
        OnPropertyChanged(nameof(HasPicture));
    }
}
