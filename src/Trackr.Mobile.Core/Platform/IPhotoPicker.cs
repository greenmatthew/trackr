namespace Trackr.Mobile.Core.Platform;

/// <summary>
/// Asks the user for a picture from the device.
/// </summary>
/// <remarks>
/// An abstraction rather than a direct call to MAUI's <c>MediaPicker</c>, so the view model
/// that orchestrates pick - downsize - upload can be exercised by plain <c>dotnet test</c>
/// (CLAUDE.md section 11). The Android implementation is the only part that needs a device.
/// <para>
/// Milestone 9 attaches meal photos to a chat message and will want the same interface, plus
/// a camera capture alongside this. Nothing here is avatar-specific for that reason.
/// </para>
/// </remarks>
public interface IPhotoPicker
{
    Task<PhotoPickResult> PickAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What came back from asking for a picture.
/// </summary>
/// <param name="Content">
/// The chosen image, which the caller owns and must dispose. Null when nothing was chosen -
/// either because the user backed out or because <paramref name="Problem"/> is set.
/// </param>
/// <param name="Problem">
/// A message fit to show the user. Null on both success and cancellation: backing out of the
/// picker is a decision, not a failure, and answering it with an error message would be
/// telling the user off for changing their mind.
/// </param>
public sealed record PhotoPickResult(Stream? Content, string? Problem)
{
    public static PhotoPickResult Cancelled { get; } = new(null, null);

    public static PhotoPickResult Picked(Stream content) => new(content, null);

    public static PhotoPickResult Failed(string problem) => new(null, problem);
}
