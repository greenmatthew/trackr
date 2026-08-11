using System.Diagnostics.CodeAnalysis;
using SkiaSharp;

namespace Trackr.Api.Cascade;

/// <summary>Why a photograph could not be turned into pixels.</summary>
/// <remarks>
/// Reported alongside the sentence shown to the user so a caller can react to a specific case -
/// a refused decompression bomb is worth a log line - without matching on wording meant for a
/// person, which would break the moment somebody improved it.
/// </remarks>
public enum ImageRefusal
{
    None,
    Empty,
    Unreadable,
    TooManyPixels
}

/// <summary>
/// Decodes an uploaded photo into pixels, refusing the ones that would exhaust the server.
/// </summary>
/// <remarks>
/// <strong>A byte limit is not a pixel limit, and this is the gap between them.</strong>
/// <c>MealImageRules.MaxBytes</c> caps an upload at 12 MB, but compression means 12 MB of JPEG can
/// describe a 20000x20000 image, which decodes to well over a gigabyte of RGBA - one request, one
/// exhausted home server. So the dimensions are read from the header first and the pixels are only
/// allocated once they are plausible.
/// <para>
/// This was written for the barcode decoder in milestone 7 and lives here because milestone 8 gave
/// it a second caller: the copy of a photo that goes to the model has to be decoded too, and having
/// two places that turn arbitrary uploaded bytes into a bitmap would mean two places to remember the
/// guard. Uploads are still stored byte-for-byte as they arrive - the decision not to re-encode on
/// ingest (docs/decisions/08-barcode-off.md) is unchanged; the guard belongs where the decoding
/// happens, and now that is two places.
/// </para>
/// </remarks>
public static class ImageGuard
{
    /// <summary>About 30 megapixels. A phone camera produces roughly 12, so this is generous.</summary>
    public const long MaxPixels = 30_000_000;

    /// <summary>
    /// Decodes, or explains why not.
    /// </summary>
    /// <remarks>
    /// Never throws for bad input - unreadable bytes are an answer, not an exception. It can still
    /// throw if Skia itself fails, which callers handle, because the alternative is swallowing a
    /// genuine fault.
    /// </remarks>
    /// <param name="problem">
    /// Null on success. Otherwise a sentence for the user: this reaches the chat, so "that image
    /// could not be read", never a codec name.
    /// </param>
    public static bool TryDecode(
        byte[] image,
        [NotNullWhen(true)] out SKBitmap? bitmap,
        out SKImageInfo info,
        [NotNullWhen(false)] out string? problem,
        out ImageRefusal refusal)
    {
        bitmap = null;
        info = default;
        problem = null;
        refusal = ImageRefusal.None;

        if (image.Length == 0)
        {
            problem = "That image was empty.";
            refusal = ImageRefusal.Empty;
            return false;
        }

        // Header first, pixels second. SKData wraps a copy of the bytes so it stays valid for as
        // long as the codec needs it.
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);

        // Null rather than throwing is how Skia reports "these bytes are not an image I know".
        if (codec is null)
        {
            problem = Unreadable;
            refusal = ImageRefusal.Unreadable;
            return false;
        }

        info = codec.Info;

        if ((long)info.Width * info.Height > MaxPixels)
        {
            problem = "That image's dimensions are too large for the server to examine.";
            refusal = ImageRefusal.TooManyPixels;
            return false;
        }

        bitmap = SKBitmap.Decode(codec);

        if (bitmap is null)
        {
            problem = Unreadable;
            refusal = ImageRefusal.Unreadable;
            return false;
        }

        return true;
    }

    private const string Unreadable =
        "That image could not be read - it may be corrupt or in a format the server does not support.";
}
