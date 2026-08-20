using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;

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
    IDeviceTimeZone deviceZone,
    INavigationService navigation) : ObservableObject
{
    /// <summary>The two energy units, in the order the picker shows them.</summary>
    private static readonly EnergyUnit[] EnergyUnits = [EnergyUnit.Kilocalories, EnergyUnit.Kilojoules];

    private static readonly MeasurementSystem[] MeasurementSystems =
        [MeasurementSystem.Metric, MeasurementSystem.Imperial];

    public string Email => session.CurrentUser?.Email ?? "not signed in";

    /// <summary>
    /// What to call the account holder, or blank to go back to the email address.
    /// </summary>
    /// <remarks>
    /// Not a second identifier. The account is still keyed by its email and still logs in with it -
    /// this is what a greeting and a set of initials read from.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedProfile))]
    public partial string NameText { get; set; } = string.Empty;

    public IReadOnlyList<string> EnergyUnitNames { get; } = ["Calories (kcal)", "Kilojoules (kJ)"];

    public IReadOnlyList<string> MeasurementSystemNames { get; } =
        ["Metric (cm, kg)", "Imperial (ft, lb)"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedProfile))]
    public partial int SelectedEnergyUnit { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnsavedProfile))]
    public partial int SelectedMeasurementSystem { get; set; }

    [ObservableProperty]
    public partial string? ProfileProblem { get; set; }

    [ObservableProperty]
    public partial bool IsProfileBusy { get; set; }

    /// <summary>
    /// Whether the form differs from the account as the server last described it.
    /// </summary>
    /// <remarks>
    /// So the save button appears when there is something to save, rather than sitting there
    /// inviting a request that would change nothing.
    /// </remarks>
    public bool HasUnsavedProfile =>
        !string.Equals(NameText.Trim(), session.CurrentUser?.DisplayName ?? "", StringComparison.Ordinal)
        || EnergyUnits[Clamp(SelectedEnergyUnit, EnergyUnits.Length)] != (session.CurrentUser?.EnergyUnit ?? EnergyUnit.Kilocalories)
        || MeasurementSystems[Clamp(SelectedMeasurementSystem, MeasurementSystems.Length)]
            != (session.CurrentUser?.MeasurementSystem ?? MeasurementSystem.Metric);

    /// <summary>
    /// Saves the name and the units together, because one route writes all three.
    /// </summary>
    /// <remarks>
    /// The units change how this app draws a number and nothing else. Every total, target and
    /// snapshot stays in kilocalories on the server - see <see cref="Nutrition.DisplayUnits"/>.
    /// </remarks>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        ProfileProblem = null;
        IsProfileBusy = true;

        try
        {
            var name = NameText.Trim();

            var saved = await api.SaveProfileAsync(new SaveProfileRequest
            {
                DisplayName = string.IsNullOrEmpty(name) ? null : name,
                EnergyUnit = EnergyUnits[Clamp(SelectedEnergyUnit, EnergyUnits.Length)],
                MeasurementSystem = MeasurementSystems[Clamp(SelectedMeasurementSystem, MeasurementSystems.Length)]
            });

            if (saved is null)
            {
                // Left showing what was typed, over an account that still says what it said. A
                // screen that redrew as though it had worked would be claiming a preference the
                // server does not hold.
                ProfileProblem = "That could not be saved, so nothing has changed.";

                return;
            }

            // What came back rather than what was sent, so the screen and the account agree even
            // if the server trimmed or refused part of it.
            if (session.CurrentUser is { } current)
            {
                await session.NoteAccountChangedAsync(current with
                {
                    DisplayName = saved.DisplayName,
                    EnergyUnit = saved.EnergyUnit,
                    MeasurementSystem = saved.MeasurementSystem
                });
            }

            ReadProfileFromSession();
        }
        finally
        {
            IsProfileBusy = false;
        }
    }

    /// <summary>The measurements behind an energy estimate, on a screen of their own.</summary>
    /// <remarks>
    /// A route rather than another card here: this page is already long, and body measurements are
    /// the most personal thing the app stores.
    /// </remarks>
    [RelayCommand]
    private Task OpenBodyMetricsAsync() => navigation.GoToBodyMetricsAsync();

    private void ReadProfileFromSession()
    {
        NameText = session.CurrentUser?.DisplayName ?? string.Empty;
        SelectedEnergyUnit = Array.IndexOf(EnergyUnits, session.CurrentUser?.EnergyUnit ?? EnergyUnit.Kilocalories);
        SelectedMeasurementSystem = Array.IndexOf(
            MeasurementSystems,
            session.CurrentUser?.MeasurementSystem ?? MeasurementSystem.Metric);

        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(HasUnsavedProfile));
    }

    private static int Clamp(int index, int length) => Math.Clamp(index, 0, length - 1);

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

        await session.NoteAccountChangedAsync(updated);

        OnPropertyChanged(nameof(TimeZoneDescription));
        OnPropertyChanged(nameof(CanUseDeviceTimeZone));
    }

    /// <summary>
    /// From the display name where there is one, and the email address otherwise.
    /// </summary>
    /// <remarks>
    /// The point of setting a name: "SW" beats the first letter of an email address on a shared
    /// server, which is the case a household member's initials are for.
    /// </remarks>
    public string Initials =>
        Avatar.InitialsFrom(session.CurrentUser?.DisplayName ?? session.CurrentUser?.Email);

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
        ReadProfileFromSession();

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
