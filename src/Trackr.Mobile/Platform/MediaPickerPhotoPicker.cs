using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Android's gallery picker, behind Core's <see cref="IPhotoPicker"/>.
/// </summary>
/// <remarks>
/// <c>PickPhotosAsync</c> rather than the single-photo <c>PickPhotoAsync</c>, which .NET MAUI
/// 10 marks obsolete in favour of it. Only the first result is used - the selection limit is
/// advisory on Android, where some pickers ignore it, so the caller has to be the one that
/// insists.
/// <para>
/// No <c>READ_MEDIA_IMAGES</c> in the manifest, deliberately. The picker hands back a URI the
/// system has already granted for the one chosen file, so broad library access would be a
/// permission the app never exercises - and CLAUDE.md section 2 is privacy-first. Capturing
/// straight from the camera is milestone 9's problem and brings its own permission with it.
/// </para>
/// </remarks>
public sealed class MediaPickerPhotoPicker : IPhotoPicker
{
    public async Task<PhotoPickResult> PickAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var picked = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
            {
                Title = "Choose a profile picture",
                SelectionLimit = 1,
            });

            if (picked.Count == 0)
            {
                return PhotoPickResult.Cancelled;
            }

            return PhotoPickResult.Picked(await picked[0].OpenReadAsync());
        }
        catch (PermissionException)
        {
            return PhotoPickResult.Failed(
                "Trackr was not allowed to open that picture. Grant access when Android asks, "
                + "or pick a different one.");
        }
        catch (FeatureNotSupportedException)
        {
            return PhotoPickResult.Failed("This device has no photo picker.");
        }
        catch (IOException)
        {
            // The picker returns a URI, and the file behind it can be gone or unreadable by
            // the time it is opened - a cloud-backed gallery entry that is not on the device
            // is the usual way.
            return PhotoPickResult.Failed("That picture could not be opened. Try another one.");
        }
    }
}
