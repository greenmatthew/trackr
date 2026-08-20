using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Shared.Auth;

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
    IServerSettings serverSettings,
    AvatarStore avatars,
    IPhotoPicker photoPicker,
    IImageDownsizer downsizer,
    ITrackrApiClient api,
    IDeviceTimeZone deviceZone) : ObservableObject
{
    public string Email => session.CurrentUser?.Email ?? "not signed in";

    /// <summary>
    /// Which zone this account's days are measured in.
    /// </summary>
    /// <remarks>
    /// The account's, not the phone's, and the difference is the whole point: the server totals a
    /// day and every client has to agree with it. Section 9.13.
    /// </remarks>
    public string TimeZoneDescription => session.CurrentUser?.TimeZoneId ?? "UTC";

    /// <summary>The zone the phone is in, offered as the answer rather than asked for.</summary>
    public string? DeviceTimeZoneId => deviceZone.IanaId;

    /// <summary>
    /// Whether offering the phone's zone would change anything.
    /// </summary>
    public bool CanUseDeviceTimeZone =>
        DeviceTimeZoneId is not null
        && !string.Equals(DeviceTimeZoneId, session.CurrentUser?.TimeZoneId, StringComparison.Ordinal);

    [ObservableProperty]
    public partial string? TimeZoneProblem { get; set; }

    [RelayCommand]
    private Task UseDeviceTimeZoneAsync() => SetTimeZoneAsync(DeviceTimeZoneId);

    [RelayCommand]
    private Task UseUtcAsync() => SetTimeZoneAsync(null);

    private async Task SetTimeZoneAsync(string? id)
    {
        TimeZoneProblem = null;

        var updated = await api.SaveTimeZoneAsync(new SaveTimeZoneRequest { TimeZoneId = id });

        if (updated is null)
        {
            // Never redrawn as though it had worked. A day quietly running on the wrong clock is
            // exactly the failure storing the zone server-side exists to prevent.
            TimeZoneProblem = "That could not be saved, so your days are unchanged.";

            return;
        }

        session.NoteAccountChanged(updated);

        OnPropertyChanged(nameof(TimeZoneDescription));
        OnPropertyChanged(nameof(CanUseDeviceTimeZone));
    }

    public string Initials => Avatar.InitialsFrom(session.CurrentUser?.Email);

    public string ServerDescription => serverSettings.BaseUrl?.Host ?? "no server configured";

    public bool TwoFactorEnabled => session.CurrentUser?.TwoFactorEnabled ?? false;

    public string TwoFactorDescription =>
        TwoFactorEnabled ? "On" : "Off - enable it on the website";

    /// <summary>The picture, or null when initials are being shown instead.</summary>
    public byte[]? Picture => avatars.Content;

    public bool HasPicture => avatars.HasPicture;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Separate from <see cref="IsBusy"/> so choosing a picture disables the picture buttons
    /// rather than the whole screen - signing out should not be blocked by an upload.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPictureBusy { get; set; }

    [ObservableProperty]
    public partial string? PictureError { get; set; }

    /// <summary>Fetches the picture if the held copy is not current. Called when the page appears.</summary>
    [RelayCommand]
    private async Task LoadAsync()
    {
        await avatars.EnsureLoadedAsync();

        NotifyPictureChanged();
    }

    /// <summary>
    /// Pick, shrink, upload.
    /// </summary>
    /// <remarks>
    /// The shrink is not an optimisation that could be skipped: a modern phone camera produces
    /// images several times the server's cap, so without it every upload from the gallery
    /// would be rejected. Re-encoding also drops the EXIF block, which on a camera photo
    /// carries the coordinates it was taken at - not something to put on a server because
    /// someone picked a profile picture.
    /// </remarks>
    [RelayCommand]
    private async Task ChangePictureAsync()
    {
        PictureError = null;

        var picked = await photoPicker.PickAsync();

        if (picked.Problem is { } problem)
        {
            PictureError = problem;

            return;
        }

        if (picked.Content is null)
        {
            // Backed out of the picker. Nothing to say.
            return;
        }

        IsPictureBusy = true;

        try
        {
            await using var source = picked.Content;

            var image = await downsizer.DownsizeAsync(source, AvatarRules.MaxEdgePixels);

            if (image is null)
            {
                PictureError = "That file could not be read as an image. Try another one.";

                return;
            }

            if (image.Content.Length > AvatarRules.MaxBytes)
            {
                // The server would reject it too. Saying so here saves the upload, and the
                // message can name the resize, which the server's 413 cannot.
                PictureError =
                    $"That picture is still over {AvatarRules.MaxBytes / 1024} KB after "
                    + "resizing. Try a different one.";

                return;
            }

            var result = await avatars.ReplaceAsync(image.Content, image.ContentType);

            PictureError = result.Succeeded ? null : result.Problem;

            NotifyPictureChanged();
        }
        finally
        {
            IsPictureBusy = false;
        }
    }

    [RelayCommand]
    private async Task RemovePictureAsync()
    {
        PictureError = null;
        IsPictureBusy = true;

        try
        {
            var result = await avatars.RemoveAsync();

            PictureError = result.Succeeded ? null : result.Problem;

            NotifyPictureChanged();
        }
        finally
        {
            IsPictureBusy = false;
        }
    }

    private void NotifyPictureChanged()
    {
        OnPropertyChanged(nameof(Picture));
        OnPropertyChanged(nameof(HasPicture));
    }

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
