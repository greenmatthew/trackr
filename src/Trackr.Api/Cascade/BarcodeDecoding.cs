namespace Trackr.Api.Cascade;

/// <summary>
/// Stage one of the cascade: find a barcode number in a photo the user attached.
/// </summary>
/// <remarks>
/// <strong>This is an internal optimisation, never a feature.</strong> CLAUDE.md section 1 keeps
/// barcodes invisible - the user photographs a jar, and whether a barcode happened to be readable
/// only changes what the server sends the model. Nothing here should ever surface as "scan a
/// barcode".
/// <para>
/// Server-side rather than on the phone, which is the choice section 9 asks to have recorded:
/// see docs/decisions/08-barcode-off.md. The short version is that the backend needs this code
/// anyway for images that arrive without a decode, so putting it on the phone as well would be two
/// implementations of one thing.
/// </para>
/// <para>
/// Behind an interface for the section 2 reason - a swappable stage - and because it lets every
/// caller be tested without a real image codec.
/// </para>
/// </remarks>
public interface IBarcodeDecoder
{
    /// <summary>
    /// Looks for a product barcode in an encoded image (JPEG, PNG, WebP, whatever the phone sent).
    /// </summary>
    /// <remarks>
    /// Synchronous on purpose. This is CPU work with no I/O to await, and wrapping it in a
    /// <see cref="Task"/> would suggest otherwise to every caller reading the signature.
    /// <para>
    /// Never throws. A photo with no barcode in it is the ordinary case, not an error, and even
    /// bytes that are not an image at all are the model's problem rather than a reason to abandon
    /// the log attempt.
    /// </para>
    /// </remarks>
    BarcodeDecodeResult Decode(byte[] image);
}

/// <summary>
/// What stage one found, and whether anything went wrong finding it.
/// </summary>
/// <param name="Barcode">
/// The digits, or null if the image had no readable barcode. A null here is not a failure - it is
/// the normal result for a photo of a plate of food.
/// </param>
/// <param name="Problem">
/// Set only when the image itself could not be processed - unreadable bytes, an unsupported codec.
/// Distinguished from "no barcode present" because section 5 wants real problems surfaced to the
/// user, and "your photo isn't a photo" is worth saying while "this meal has no barcode" is not.
/// </param>
public sealed record BarcodeDecodeResult(string? Barcode, string? Problem = null)
{
    /// <remarks>
    /// The argument is named because it has to be: a record's generated copy constructor also takes
    /// one reference parameter, and it wins overload resolution against a primary constructor whose
    /// second parameter is optional.
    /// </remarks>
    public static readonly BarcodeDecodeResult NoBarcode = new(Barcode: null);

    public static BarcodeDecodeResult Found(string barcode) => new(barcode);

    public static BarcodeDecodeResult Unreadable(string problem) => new(null, problem);

    /// <summary>True when there is a number worth asking Open Food Facts about.</summary>
    public bool HasBarcode => Barcode is not null;
}
