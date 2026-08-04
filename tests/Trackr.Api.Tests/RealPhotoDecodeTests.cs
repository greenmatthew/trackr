using Microsoft.Extensions.Logging.Abstractions;
using Trackr.Api.Cascade;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The decoder against real photographs, from <c>media/examples</c>.
/// </summary>
/// <remarks>
/// <strong>This is the suite <see cref="BarcodeDecoderTests"/> cannot be.</strong> That one renders
/// its own barcodes, which are square-on, evenly lit and perfectly flat; it proves the library is
/// wired up. These are photographs and product shots of real packaging - a barcode wrapped around a
/// curved can, a UPC printed small on a wide carton, and label photos with no barcode in frame at
/// all - so this is the only place the decode rate means anything.
/// <para>
/// Two kinds of assertion, and the second matters more than the first:
/// <list type="bullet">
/// <item>Photos with a barcode decode to the <em>right</em> number.</item>
/// <item><strong>Photos without one decode to nothing.</strong> A false positive is the worst
/// outcome the cascade can produce, far worse than a miss: a plausible wrong number gets looked up,
/// may well match some unrelated product, and is then reported as a full match - which is exactly
/// the branch that does <em>not</em> send the photo to the model. The user is shown someone else's
/// food with no warning attached. Measuring this is what removed UPC-E from the format list.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class RealPhotoDecodeTests
{
    private readonly ZXingBarcodeDecoder _decoder = new(NullLogger<ZXingBarcodeDecoder>.Instance);

    /// <remarks>
    /// The expected values are checkable by eye rather than taken on trust: the Peace Tea can's code
    /// is in its own filename, and the other two are legible in the images.
    /// </remarks>
    [Theory]
    // A curved can, barcode wrapped around the cylinder and printed sideways. Only decodes on the
    // second pass, at double resolution - it is the reason that pass exists.
    [InlineData("barcode/00049000557695_C7N1.png", "049000557695")]
    // A tall bottle shot, barcode flat and square-on.
    [InlineData("barcode/a7830573-a4be-4299-b8f2-ff98f7fe541c.72af9627c049dddb318c997a0b0868b8.jpeg", "076840220311")]
    // A carton back: barcode alongside a full nutrition table and an ingredients paragraph, which is
    // the case most likely to produce a spurious read.
    [InlineData("barcode+nutrition-facts+ingredients/b4665c191b34baf3d0e0fa45dfdd3d1d.jpeg", "049000070484")]
    public void A_photographed_barcode_decodes_to_the_right_number(string path, string expected)
    {
        Assert.Equal(expected, _decoder.Decode(Photo(path)).Barcode);
    }

    /// <remarks>
    /// Every one of these decoded to a plausible eight-digit number while UPC-E was in the format
    /// list, on packaging whose barcode is not in the frame. If this test ever fails, the decoder has
    /// started inventing barcodes and the fix is not to relax the test.
    /// </remarks>
    [Theory]
    [InlineData("ingredients/7aa7ecc8-5cc8-4ee0-ab56-9704c75cb556.119d4b639cd30da5f458153bcb649ce0.webp")]
    [InlineData("nutrition-facts/7a0255d0-bef0-4adf-aa40-e2b70e5d6920.5c0a13c030f35b6a537649bc29fe6e4f.webp")]
    [InlineData("nutrition-facts/Screenshot 2025-09-14 192138.png")]
    public void A_label_photo_with_no_barcode_invents_nothing(string path)
    {
        var result = _decoder.Decode(Photo(path));

        Assert.Null(result.Barcode);

        // Nor is it an error: a photo of a label is a perfectly good thing to send the model.
        Assert.Null(result.Problem);
    }

    /// <summary>
    /// A barcode this decoder currently cannot read, recorded rather than quietly omitted.
    /// </summary>
    /// <remarks>
    /// The Little Debbie carton's UPC-A is flat, sharp and unobstructed, and still misses: the frame
    /// is 2000x2000 of mostly packaging artwork, and the barcode occupies a couple of hundred pixels
    /// of it, so the bars are nearly as thin as the sampling grid. Doubling does not rescue it because
    /// the retry is bounded to smaller images, and tiling and downscaling were both measured and
    /// rejected - they bought no true positives and several false ones.
    /// <para>
    /// <strong>A failure here is good news.</strong> It means something improved the decode rate, and
    /// the right response is to move this case into the theory above with its real number,
    /// <c>024300041068</c>, which is legible in the image.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_small_barcode_on_a_large_carton_is_a_known_miss()
    {
        var photo = Photo(
            "barcode+nutrition-facts+ingredients/"
                + "42f72b48-8239-4bca-b941-6fef155ca2aa.7aa1e9a11cdad7421cb35a0531e336ea.webp");

        Assert.Null(_decoder.Decode(photo).Barcode);
    }

    /// <remarks>
    /// Reads from the repository rather than copying the photos into the test output: they are
    /// example material for the whole project, not fixtures owned by this suite, and some are a
    /// third of a megabyte.
    /// </remarks>
    private static byte[] Photo(string relativePath)
    {
        var directory = AppContext.BaseDirectory;

        while (!Directory.Exists(Path.Combine(directory, "media", "examples")))
        {
            directory = Path.GetDirectoryName(directory)
                ?? throw new DirectoryNotFoundException(
                    "Could not find media/examples by walking up from the test assembly.");
        }

        return File.ReadAllBytes(Path.Combine(directory, "media", "examples", relativePath));
    }
}
