namespace Trackr.Shared.Nutrition;

/// <summary>
/// The rules both ends of a meal-photo upload have to agree on.
/// </summary>
/// <remarks>
/// Mirrors <c>Auth/AvatarRules</c>, and differs from it in one deliberate way: there is no
/// <c>MaxEdgePixels</c>. A profile picture is downsized because it is only ever drawn small; a
/// meal photo is kept at full sensor resolution so re-running a better model over it later is
/// never foreclosed.
/// <para>
/// The phone encodes to WebP at quality 90 before uploading - Android's
/// <c>Bitmap.Compress</c> does it with no extra package. That is a convention, not an invariant:
/// the server accepts JPEG and PNG too and stores exactly what arrives, because a share target or
/// a future non-MAUI client may send either.
/// </para>
/// <para>
/// This once said nothing on the server decodes the bytes, which milestone 7 made false: the barcode
/// stage of the cascade decodes them. What still holds, and is the part that matters, is that
/// <strong>the stored bytes are never re-encoded</strong> - an upload is kept exactly as it arrived.
/// The decoder guards itself instead, by refusing implausible dimensions before allocating pixels.
/// </para>
/// </remarks>
public static class MealImageRules
{
    /// <summary>
    /// Hard cap on the uploaded body, in bytes.
    /// </summary>
    /// <remarks>
    /// A 12MP camera JPEG reaches 6-8 MB and the same frame as WebP q90 lands near 3 MB, so 12 MB
    /// has real headroom without being an open door.
    /// </remarks>
    public const int MaxBytes = 12 * 1024 * 1024;

    /// <summary>
    /// The content types the server stores.
    /// </summary>
    /// <remarks>
    /// An allow-list, and the same short one the avatar uses. SVG in particular is a document that
    /// can carry script, and these bytes are handed straight back to whatever renders them.
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

/// <summary>A stored meal photo, without its bytes.</summary>
/// <remarks>
/// The bytes are never in a JSON body - they come from <c>GET /api/images/{id}</c>, exactly as the
/// avatar does. A log entry can carry several photos, and putting megabytes of base64 into every
/// log response would be the quiet way to make the stats views slow.
/// </remarks>
/// <param name="ByteCount">The stored size, so a listing never has to read the blob to report it.</param>
public sealed record MealImageResponse(
    Guid Id,
    string ContentType,
    int ByteCount,
    DateTimeOffset CreatedUtc);
