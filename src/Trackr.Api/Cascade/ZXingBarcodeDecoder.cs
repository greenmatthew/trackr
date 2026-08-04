using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Trackr.Api.Cascade;

/// <summary>
/// Reads product barcodes out of photos with ZXing, decoding the image itself with SkiaSharp.
/// </summary>
/// <remarks>
/// Both libraries are licence-compatible with AGPL-3.0 per CLAUDE.md section 10: ZXing.Net is
/// Apache-2.0 and SkiaSharp is MIT.
/// <para>
/// <strong>SkiaSharp needs a native library, and the base image is Alpine.</strong> The API ships on
/// <c>aspnet:10.0-alpine</c>, which is musl rather than glibc, so the project references
/// <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c> - it carries a <c>linux-musl-x64</c> build,
/// and the "NoDependencies" variant needs no fontconfig, which matters because nothing here draws
/// text. Dropping either reference turns every decode into a <c>DllNotFoundException</c> at runtime
/// with a green build, so please do not tidy them away.
/// </para>
/// </remarks>
public sealed class ZXingBarcodeDecoder(ILogger<ZXingBarcodeDecoder> logger) : IBarcodeDecoder
{
    /// <summary>
    /// The formats that appear on retail food packaging, and nothing else.
    /// </summary>
    /// <remarks>
    /// A closed list rather than "try everything", for accuracy rather than speed: the 1D formats
    /// ZXing supports include several - ITF and Code 39 especially - that happily find spurious
    /// barcodes in the busy stripes of a real product photo. A wrong number would be looked up and
    /// return a confidently wrong product, which is worse than no number at all, because the wrong
    /// product silently skips the image-to-model step that would have caught it.
    /// <para>
    /// <strong>UPC-E is deliberately absent, and that is a measured decision rather than an
    /// oversight.</strong> Against the real photographs in <c>media/examples</c> it was the sole
    /// source of false positives: it found checksum-valid eight-digit codes in an ingredients
    /// paragraph and in a nutrition table, on packets whose barcode was not in the frame at all. It
    /// carries only six digits of data, so dense small text produces valid-looking patterns by
    /// chance. The cost of dropping it is that a UPC-E packet - the small ones, gum and travel sizes -
    /// falls through to the model instead. That is the right way round: a miss costs a slower answer,
    /// a false positive costs a wrong one that looks authoritative.
    /// </para>
    /// <para>
    /// UPC-A comes back as twelve digits and EAN-13 as thirteen. Neither is padded here: Open Food
    /// Facts normalises the length itself, and a wrong guess about which leading zero to add would
    /// turn a findable product into a miss.
    /// </para>
    /// </remarks>
    private static readonly BarcodeFormat[] ProductFormats =
    [
        BarcodeFormat.EAN_13,
        BarcodeFormat.EAN_8,
        BarcodeFormat.UPC_A
    ];

    /// <summary>
    /// Images up to this size get a second attempt at double resolution.
    /// </summary>
    /// <remarks>
    /// Small images are where a barcode's bars land close to a single pixel each, and interpolating
    /// them up gives ZXing edges it can actually find. This is not hypothetical tuning: a 500x500
    /// product shot of a curved can in <c>media/examples</c> is undecodable at full size and reads
    /// correctly at double.
    /// <para>
    /// The threshold does two jobs. It bounds the memory - four times the pixels, so 4 MP in means
    /// 16 MP allocated - and it keeps the retry away from phone photographs, which arrive at around
    /// 12 MP and have no sub-pixel bars to rescue. That matters because most meals are not packaged,
    /// so a photo with no barcode in it is the common case, and it would otherwise pay for a second
    /// full decode every time to discover the same nothing.
    /// </para>
    /// </remarks>
    private const long MaxRetryPixels = 4_000_000;

    /// <summary>
    /// The most pixels this will decode, about 30 megapixels.
    /// </summary>
    /// <remarks>
    /// <strong>A byte limit is not a pixel limit, and this is the gap between them.</strong>
    /// <c>MealImageRules.MaxBytes</c> caps an upload at 12 MB, but compression means 12 MB of JPEG
    /// can describe a 20000x20000 image, which decodes to well over a gigabyte of RGBA - one
    /// request, one exhausted home server. So the dimensions are read from the header first and the
    /// pixels are only allocated if they are plausible. A phone camera produces about 12 MP, so this
    /// leaves generous headroom.
    /// <para>
    /// This is the answer to the note left on <c>ImageEndpoints</c>, which recorded that the server
    /// had no image decoder and that this milestone would be the moment to revisit it. It is
    /// revisited, and the conclusion is narrow: uploads are still stored byte-for-byte as they
    /// arrive, because the model wants the original photo and re-encoding on ingest would degrade it
    /// for every user to guard a decoder that only this class runs. The guard belongs where the
    /// decoding happens.
    /// </para>
    /// </remarks>
    private const long MaxPixels = 30_000_000;

    public BarcodeDecodeResult Decode(byte[] image)
    {
        if (image.Length == 0)
        {
            return BarcodeDecodeResult.Unreadable("That image was empty.");
        }

        SKBitmap? bitmap = null;

        try
        {
            // Header first, pixels second. SKData wraps a copy of the bytes so it stays valid for as
            // long as the codec needs it.
            using var data = SKData.CreateCopy(image);
            using var codec = SKCodec.Create(data);

            // Null rather than throwing is how Skia reports "these bytes are not an image I know".
            if (codec is null)
            {
                return BarcodeDecodeResult.Unreadable(
                    "That image could not be read - it may be corrupt or in a format the server "
                        + "does not support.");
            }

            var pixels = (long)codec.Info.Width * codec.Info.Height;

            if (pixels > MaxPixels)
            {
                logger.LogWarning(
                    "Refused to decode a {Width}x{Height} image for barcodes.",
                    codec.Info.Width,
                    codec.Info.Height);

                return BarcodeDecodeResult.Unreadable(
                    "That image's dimensions are too large for the server to examine.");
            }

            bitmap = SKBitmap.Decode(codec);

            if (bitmap is null)
            {
                return BarcodeDecodeResult.Unreadable(
                    "That image could not be read - it may be corrupt or in a format the server "
                        + "does not support.");
            }

            var result = Read(bitmap);

            // Second attempt at double resolution, for the small images whose bars are about a pixel
            // wide. Only ever a fallback: it is pure cost when the first pass already succeeded, and
            // pure cost again on the large photos it is bounded away from.
            if (result?.Text is null && pixels <= MaxRetryPixels)
            {
                using var doubled = bitmap.Resize(
                    new SKImageInfo(bitmap.Width * 2, bitmap.Height * 2),
                    new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

                if (doubled is not null)
                {
                    result = Read(doubled);

                    if (result?.Text is not null)
                    {
                        logger.LogDebug("Decoded a barcode only after doubling the image.");
                    }
                }
            }

            if (result?.Text is null)
            {
                return BarcodeDecodeResult.NoBarcode;
            }

            var digits = new string(result.Text.Where(char.IsAsciiDigit).ToArray());

            // A product barcode that decoded to something other than digits is a misread, not a
            // find. Passing it on would mean an Open Food Facts request that cannot match anything.
            if (digits.Length != result.Text.Length || digits.Length is < 8 or > 14)
            {
                logger.LogDebug(
                    "Discarded a {Format} decode that is not a product barcode.", result.BarcodeFormat);

                return BarcodeDecodeResult.NoBarcode;
            }

            logger.LogDebug("Decoded a {Format} barcode from an image.", result.BarcodeFormat);

            return BarcodeDecodeResult.Found(digits);
        }
        catch (Exception exception)
        {
            // Deliberately broad. A decode runs a third-party library over bytes that arrived from
            // a phone camera, and the contract on IBarcodeDecoder is that stage one never aborts a
            // log attempt - whatever went wrong, the image can still go to the model.
            logger.LogWarning(exception, "Barcode decoding threw; treating the image as undecodable.");

            return BarcodeDecodeResult.Unreadable("The server could not read that image.");
        }
        finally
        {
            bitmap?.Dispose();
        }
    }

    /// <summary>One decoding pass over a bitmap.</summary>
    /// <remarks>
    /// A fresh reader per pass rather than a shared one: <c>BarcodeReader</c> carries per-call state,
    /// and this class is registered as a singleton serving concurrent requests.
    /// </remarks>
    private static Result? Read(SKBitmap bitmap)
    {
        var reader = new ZXing.SkiaSharp.BarcodeReader
        {
            // TryHarder trades time for hit rate, which is the right trade here: the alternative to a
            // slower decode is sending the photo to the model, which costs far more.
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats = ProductFormats
            },

            // A photo of a jar is as likely to hold the barcode sideways as level.
            AutoRotate = true
        };

        return reader.Decode(bitmap);
    }
}
