namespace Trackr.Shared.Auth;

/// <summary>
/// The rules both ends of the avatar upload have to agree on.
/// </summary>
/// <remarks>
/// Shared rather than duplicated so the phone can downsize to something it knows the server
/// will accept, instead of discovering the limit by being rejected. The server still enforces
/// every one of these - a client-side check is a courtesy, not a control.
/// </remarks>
public static class AvatarRules
{
    /// <summary>
    /// The longest edge, in pixels, a client should downsize to before uploading.
    /// </summary>
    /// <remarks>
    /// The picture is drawn at 36px in the title bar and 72px on the profile, so 512 is
    /// already generous - it leaves room for a denser screen and for cropping later without
    /// making a round trip to the original.
    /// </remarks>
    public const int MaxEdgePixels = 512;

    /// <summary>
    /// Hard cap on the uploaded body, in bytes.
    /// </summary>
    /// <remarks>
    /// A 512px JPEG lands in the tens of kilobytes, so 512 KB is roughly ten times the
    /// expected size: comfortably past anything legitimate, and far short of what would make
    /// a row awkward to store or a backup noticeably larger.
    /// </remarks>
    public const int MaxBytes = 512 * 1024;

    /// <summary>
    /// The content types the server stores.
    /// </summary>
    /// <remarks>
    /// An allow-list rather than a block-list, and deliberately short. Anything not on it is
    /// rejected outright: the bytes are handed straight back to whatever renders them later,
    /// so accepting SVG in particular would be accepting a document that can carry script.
    /// </remarks>
    public static readonly string[] AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
    ];

    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null
        && AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Result of uploading a profile picture.</summary>
/// <param name="UpdatedUtc">
/// The new marker, matching what <c>GET /api/auth/me</c> will report from now on. Returned so
/// the caller can update its cache without a second round trip.
/// </param>
public sealed record AvatarResponse(DateTimeOffset UpdatedUtc);
