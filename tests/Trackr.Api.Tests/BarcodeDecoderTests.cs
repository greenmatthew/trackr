using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Trackr.Api.Cascade;
using ZXing;
using ZXing.Common;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The barcode decoder, against images generated in the test rather than photographed.
/// </summary>
/// <remarks>
/// <strong>What this suite proves is narrow, and worth being honest about.</strong> A rendered
/// barcode is a perfect barcode: square-on, evenly lit, in focus, no glare, no curved jar. Passing
/// here means the library is wired up, the native musl build of Skia loaded, the format list is
/// right and the digits come back intact. It says nothing about whether a real photo of a real
/// cupboard decodes, which only a real photo can answer - see docs/decisions/08-barcode-off.md.
/// <para>
/// The decoder is also the one part of the cascade that touches an image codec, so the cases below
/// spend as much attention on malformed input as on the happy path.
/// </para>
/// </remarks>
public sealed class BarcodeDecoderTests
{
    private readonly ZXingBarcodeDecoder _decoder = new(NullLogger<ZXingBarcodeDecoder>.Instance);

    /// <summary>Nutella's real EAN-13, so the check digit is genuinely valid.</summary>
    private const string Ean13 = "3017620422003";

    [Fact]
    public void An_ean_13_barcode_is_read_back()
    {
        var result = _decoder.Decode(Render(Ean13, BarcodeFormat.EAN_13));

        Assert.Equal(Ean13, result.Barcode);
        Assert.Null(result.Problem);
        Assert.True(result.HasBarcode);
    }

    [Fact]
    public void A_upc_a_barcode_is_read_back()
    {
        // A 12-digit UPC-A, as a US product carries. Reported at its own length rather than padded
        // to 13 - Open Food Facts normalises that itself.
        const string upc = "038000138416";

        var result = _decoder.Decode(Render(upc, BarcodeFormat.UPC_A));

        Assert.Equal(upc, result.Barcode);
    }

    /// <remarks>
    /// The ordinary case for this app: most meals are not packaged, so most photos have no barcode.
    /// It must not look like an error, or every plate of food would raise a warning on the card.
    /// </remarks>
    [Fact]
    public void A_photo_with_no_barcode_is_not_an_error()
    {
        var result = _decoder.Decode(Blank(400, 300));

        Assert.Null(result.Barcode);
        Assert.Null(result.Problem);
    }

    /// <remarks>
    /// Excluded from the format list on purpose: Code 128 and friends find spurious barcodes in the
    /// stripes of real packaging, and a wrong number would be looked up and answered confidently.
    /// </remarks>
    [Fact]
    public void A_barcode_in_a_format_food_does_not_use_is_ignored()
    {
        var result = _decoder.Decode(Render("SHIPPING-1234", BarcodeFormat.CODE_128));

        Assert.Null(result.Barcode);
    }

    [Fact]
    public void Bytes_that_are_not_an_image_are_reported_as_unreadable()
    {
        var result = _decoder.Decode(Encoding.UTF8.GetBytes("this is not a photograph"));

        Assert.Null(result.Barcode);
        Assert.NotNull(result.Problem);
    }

    [Fact]
    public void An_empty_image_is_reported_as_unreadable()
    {
        var result = _decoder.Decode([]);

        Assert.NotNull(result.Problem);
    }

    /// <remarks>
    /// The decompression-bomb guard. This PNG is a few dozen bytes and claims to be 20000x20000,
    /// which is over a gigabyte of pixels - the gap between the 12 MB upload cap and what a decoder
    /// would actually allocate. The point of the test is that the refusal comes from reading the
    /// header, because a decoder that had to allocate the pixels to find out would already have lost.
    /// </remarks>
    [Fact]
    public void An_image_claiming_enormous_dimensions_is_refused_without_being_decoded()
    {
        var result = _decoder.Decode(PngHeaderOnly(20_000, 20_000));

        Assert.Null(result.Barcode);
        Assert.NotNull(result.Problem);
        Assert.Contains("dimensions", result.Problem);
    }

    /// <summary>Renders a barcode to PNG bytes, the way a phone would have sent a photo of one.</summary>
    private static byte[] Render(string content, BarcodeFormat format)
    {
        var writer = new ZXing.SkiaSharp.BarcodeWriter
        {
            Format = format,
            Options = new EncodingOptions
            {
                Width = 600,
                Height = 300,

                // A quiet zone is part of the spec, and without one the decode legitimately fails.
                Margin = 20
            }
        };

        using var bitmap = writer.Write(content);

        return Encode(bitmap);
    }

    private static byte[] Blank(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        return Encode(bitmap);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    /// <summary>
    /// A PNG signature and IHDR chunk declaring the given dimensions, and no pixel data at all.
    /// </summary>
    private static byte[] PngHeaderOnly(int width, int height)
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: truecolour
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering
        header[12] = 0; // no interlace

        png.AddRange(Chunk("IHDR", header));

        // A short but genuinely valid zlib stream. Skia will not build a codec from a header alone -
        // it wants pixel data to exist before it will tell anybody the dimensions - and this is
        // nowhere near enough bytes to fill the claimed image, which is the point: the file is tiny
        // and its declared size is not.
        png.AddRange(Chunk("IDAT", Deflate(new byte[1024])));
        png.AddRange(Chunk("IEND", []));

        return [.. png];
    }

    private static byte[] Deflate(byte[] data)
    {
        using var buffer = new MemoryStream();

        using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return buffer.ToArray();
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type).CopyTo(typeAndData, 0);
        data.CopyTo(typeAndData, 4);

        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);

        // PNG chunks carry a CRC-32 over the type and data. Skia rejects the file outright without a
        // correct one, which would make this test pass for the wrong reason.
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));

        return [.. length, .. typeAndData, .. crc];
    }

    /// <summary>
    /// CRC-32, by hand rather than from <c>System.IO.Hashing</c>.
    /// </summary>
    /// <remarks>
    /// Twelve lines beats adding a package to the test project for one checksum in one test.
    /// </remarks>
    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var b in bytes)
        {
            crc ^= b;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
