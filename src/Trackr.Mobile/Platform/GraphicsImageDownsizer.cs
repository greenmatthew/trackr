using Microsoft.Extensions.Logging;
using Microsoft.Maui.Graphics.Platform;
using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Shrinks and re-encodes with <c>Microsoft.Maui.Graphics</c>, which ships with MAUI.
/// </summary>
/// <remarks>
/// Always re-encodes, even when the image is already small enough. Two reasons beyond size:
/// the output content type becomes something the server's allow-list definitely accepts
/// whatever the gallery held, and the EXIF block is dropped - a camera photo carries the
/// coordinates it was taken at, which has no business travelling with a profile picture.
/// <para>
/// JPEG rather than PNG. A photograph is several times larger as PNG, and the cost is
/// transparency, which a picture cropped into a circle has no use for.
/// </para>
/// </remarks>
public sealed class GraphicsImageDownsizer(ILogger<GraphicsImageDownsizer> logger) : IImageDownsizer
{
    private const float JpegQuality = 0.9f;

    public async Task<DownsizedImage?> DownsizeAsync(
        Stream source,
        int maxEdgePixels,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var original = PlatformImage.FromStream(source);
            using var resized = original.Downsize(maxEdgePixels, disposeOriginal: false);

            using var encoded = new MemoryStream();
            await resized.SaveAsync(encoded, ImageFormat.Jpeg, JpegQuality);

            return new DownsizedImage(encoded.ToArray(), "image/jpeg");
        }
        catch (Exception ex)
        {
            // Broader than the closed lists elsewhere in this codebase, and on purpose: what
            // the platform decoder throws for a file that is not really an image varies by
            // device and by format, and the one thing that must not happen is the app dying
            // because someone picked an oddity out of their gallery. The caller turns null
            // into "that file could not be read as an image".
            logger.LogWarning(ex, "Could not decode the chosen image");

            return null;
        }
    }
}
