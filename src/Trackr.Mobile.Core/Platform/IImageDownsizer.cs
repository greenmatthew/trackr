namespace Trackr.Mobile.Core.Platform;

/// <summary>
/// Shrinks and re-encodes an image before it is uploaded.
/// </summary>
/// <remarks>
/// Client-side rather than server-side, deliberately. A phone camera produces several
/// megabytes and the server stores a picture drawn at 72 pixels, so resizing on the phone
/// saves the upload rather than just the storage - which is the part that costs anything on a
/// mobile connection. It also means the server never has to decode attacker-controlled image
/// bytes, which is a category of parser bug worth not having.
/// <para>
/// <c>SixLabors.ImageSharp</c> is the obvious library for the job and is ruled out: the Six
/// Labors Split License is source-available, which CLAUDE.md section 10 excludes. The Android
/// implementation uses <c>Microsoft.Maui.Graphics</c>, which ships with MAUI and adds nothing.
/// </para>
/// </remarks>
public interface IImageDownsizer
{
    /// <summary>
    /// Re-encodes <paramref name="source"/> so its longest edge is at most
    /// <paramref name="maxEdgePixels"/>.
    /// </summary>
    /// <returns>
    /// The re-encoded image, or null when the bytes could not be decoded as an image at all.
    /// </returns>
    Task<DownsizedImage?> DownsizeAsync(
        Stream source,
        int maxEdgePixels,
        CancellationToken cancellationToken = default);
}

/// <param name="Content">The re-encoded bytes, ready to upload.</param>
/// <param name="ContentType">
/// What the bytes actually are now, which is not necessarily what went in - the point of the
/// step is to normalise whatever the gallery held into something the server accepts.
/// </param>
public sealed record DownsizedImage(byte[] Content, string ContentType);
