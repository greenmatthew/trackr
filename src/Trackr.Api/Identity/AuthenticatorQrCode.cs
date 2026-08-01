using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Net.Codecrete.QrCodeGenerator;

namespace Trackr.Api.Identity;

/// <summary>
/// Builds what an authenticator app needs in order to enrol: the otpauth URI, a scannable
/// QR code, and the secret in a form a human can type if the camera will not cooperate.
/// </summary>
public static class AuthenticatorQrCode
{
    /// <summary>Shown in the authenticator app's account list.</summary>
    private const string Issuer = "Trackr";

    /// <summary>
    /// The URI layout every authenticator app understands - the same one Identity's own
    /// scaffolded 2FA page produces.
    /// </summary>
    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    /// <param name="email">Labels the entry in the authenticator app.</param>
    /// <param name="unformattedKey">
    /// The base32 secret from <c>UserManager.GetAuthenticatorKeyAsync</c>.
    /// </param>
    public static string BuildUri(string email, string unformattedKey) =>
        string.Format(
            CultureInfo.InvariantCulture,
            AuthenticatorUriFormat,
            UrlEncoder.Default.Encode(Issuer),
            UrlEncoder.Default.Encode(email),
            unformattedKey);

    /// <summary>
    /// Renders the URI as an SVG QR code wrapped in a data URI, ready for an img src.
    /// </summary>
    /// <remarks>
    /// SVG rather than a bitmap so it stays sharp on a phone screen, and a data URI rather
    /// than returning raw markup so the client never renders server-supplied HTML.
    /// Error-correction level Medium is the usual choice for otpauth codes: it tolerates a
    /// smudged screen without inflating the code so much that it gets hard to scan.
    /// </remarks>
    public static string BuildSvgDataUri(string authenticatorUri)
    {
        var svg = QrCode.EncodeText(authenticatorUri, QrCode.Ecc.Medium).ToSvgString(border: 2);

        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>
    /// Groups the secret into blocks of four so it can be read aloud or typed by hand.
    /// </summary>
    public static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();

        for (var position = 0; position < unformattedKey.Length; position += 4)
        {
            if (position > 0)
            {
                result.Append(' ');
            }

            result.Append(unformattedKey.AsSpan(position, Math.Min(4, unformattedKey.Length - position)));
        }

        return result.ToString();
    }
}
