using SkiaSharp;

namespace Trackr.Api.Cascade;

/// <summary>
/// Makes the copy of a photograph that goes to the model.
/// </summary>
/// <remarks>
/// <strong>This is not an optimisation, and skipping it does not merely make things slow - it makes
/// them silently wrong.</strong> Ollama sizes its context window from available video memory, so a
/// CPU-only server gets the smallest one, and a 12-megapixel photograph turns into more visual
/// tokens than fit in it. Ollama's response to a prompt that does not fit is to truncate the
/// <em>oldest</em> tokens and carry on without reporting anything, and the oldest tokens are the
/// system prompt and the schema. See <see cref="OllamaOptions.ContextLength"/>.
/// <para>
/// It also happens to be most of the wall-clock time: prefill on a CPU is roughly linear in the
/// number of visual tokens.
/// </para>
/// <para>
/// <strong>The stored bytes are untouched.</strong> docs/decisions/08-barcode-off.md refused to
/// re-encode photos on ingest, so that a better model can be run over the original later, and this
/// does not reverse that - it resizes a copy on the way to one consumer. The barcode decoder still
/// reads the original, because its hit rate was measured there and one of its three successes needed
/// the full resolution.
/// </para>
/// </remarks>
public static class MealImageDownscaler
{
    /// <summary>
    /// High enough that JPEG artefacts do not eat the small print on a nutrition panel.
    /// </summary>
    /// <remarks>
    /// Reading small print is the entire job, so this is the wrong place to save bytes - the
    /// dimensions in <see cref="OllamaOptions.MaxImageEdgePixels"/> are where the size actually
    /// comes down, and those were judged against the photographs in <c>media/examples</c>.
    /// </remarks>
    private const int Quality = 85;

    /// <summary>
    /// JPEG, because it is the format every vision model's image loader definitely understands.
    /// </summary>
    /// <remarks>
    /// Not a compression preference. <c>MealImageRules</c> accepts WebP - the phone may well send it
    /// - and the stb_image loader underneath llama.cpp does not read WebP at all, so a WebP passed
    /// through untouched would be rejected or silently ignored by the model rather than by anything
    /// here. Normalising to JPEG removes a whole class of "it works with my photos" bug.
    /// </remarks>
    private const string JpegContentType = "image/jpeg";

    /// <summary>
    /// The base64 the model is sent, or null with a reason.
    /// </summary>
    /// <remarks>
    /// Raw base64 with no <c>data:</c> prefix, which is what Ollama's <c>images</c> array wants.
    /// </remarks>
    /// <param name="maxEdgePixels">The longest edge to allow. See <see cref="OllamaOptions.MaxImageEdgePixels"/>.</param>
    public static string? ToBase64(
        byte[] image,
        string contentType,
        int maxEdgePixels,
        out string? problem)
    {
        SKBitmap? bitmap = null;

        try
        {
            if (!ImageGuard.TryDecode(image, out bitmap, out var info, out problem, out _))
            {
                return null;
            }

            var longest = Math.Max(info.Width, info.Height);

            // A small JPEG is already exactly what is wanted, so re-encoding it would spend quality
            // to achieve nothing. Any other format is re-encoded whatever its size - see the note on
            // JpegContentType.
            if (longest <= maxEdgePixels
                && contentType.Equals(JpegContentType, StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToBase64String(image);
            }

            var scale = longest <= maxEdgePixels ? 1d : (double)maxEdgePixels / longest;
            var width = Math.Max(1, (int)Math.Round(info.Width * scale));
            var height = Math.Max(1, (int)Math.Round(info.Height * scale));

            // Drawn onto an opaque white surface rather than encoded directly, because JPEG has no
            // alpha channel: a screenshot with a transparent background would otherwise come out
            // composited onto black, which is a photograph of nothing.
            using var surface = SKSurface.Create(
                new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));

            if (surface is null)
            {
                problem = "That image could not be prepared for the local model.";
                return null;
            }

            surface.Canvas.Clear(SKColors.White);

            using (var source = SKImage.FromBitmap(bitmap))
            {
                surface.Canvas.DrawImage(
                    source,
                    new SKRect(0, 0, width, height),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }

            using var snapshot = surface.Snapshot();
            using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, Quality);

            if (encoded is null)
            {
                problem = "That image could not be prepared for the local model.";
                return null;
            }

            return Convert.ToBase64String(encoded.AsSpan());
        }
        finally
        {
            bitmap?.Dispose();
        }
    }
}
