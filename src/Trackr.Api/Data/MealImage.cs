using Trackr.Api.Identity;

namespace Trackr.Api.Data;

/// <summary>
/// A photo of a meal. Personal to the account that uploaded it, and never shared.
/// </summary>
/// <remarks>
/// <strong><see cref="LogEntryId"/> is nullable on purpose, and that is what makes milestone 9
/// cheap.</strong> The chat flow is upload, then cascade, then confirm: the photo has to exist on
/// the server before the log entry does, so the model can be retried without a second upload and
/// so an abandoned confirmation leaves no orphaned entry behind. <c>POST /api/log</c> then adopts
/// images by id. Unclaimed images are swept in milestone 14, which is what the
/// (UserId, CreatedUtc) index is for.
/// <para>
/// Bytes are stored exactly as uploaded - full resolution, no downscaling, no re-encoding - so
/// that re-running a better model over an old photo is never foreclosed. The phone encodes WebP
/// q90 before uploading, but that is a convention rather than an invariant: JPEG and PNG are
/// accepted too, and <strong>nothing on the server decodes the bytes</strong>. Image decoders are
/// a well-known remote-code-execution and denial-of-service surface, and the avatar path already
/// set the precedent of keeping that surface at zero.
/// </para>
/// <para>
/// In Postgres as <c>bytea</c> rather than on a volume, for the reason
/// wiki/Backup-and-Restore.md depends on: <c>pg_dump</c> stays the single backup artifact, there
/// is no second consistency window, and an orphaned file on disk cannot happen. The honest cost is
/// dump size, which that page now warns about.
/// </para>
/// </remarks>
public class MealImage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// The uploader. Required, and the only thing that decides who may read the image.
    /// </summary>
    /// <remarks>
    /// Ownership is recorded on the image itself rather than inferred through
    /// <see cref="LogEntryId"/>, because for most of an image's life - between upload and confirm -
    /// there is no entry to infer it from.
    /// </remarks>
    public Guid UserId { get; set; }

    public TrackrUser? User { get; set; }

    /// <summary>The entry that adopted it, or null while it is still unattached.</summary>
    /// <remarks>
    /// Cascade, so deleting an entry deletes its photos - they have no other purpose. Cascade only
    /// fires for non-null foreign keys, so unattached images are untouched by it.
    /// </remarks>
    public Guid? LogEntryId { get; set; }

    public LogEntry? LogEntry { get; set; }

    /// <summary>Echoed back on GET, so a client renders what was actually stored.</summary>
    public string ContentType { get; set; } = "";

    public byte[] Content { get; set; } = [];

    /// <summary>
    /// The stored size, so listing an entry's images never has to read the blobs.
    /// </summary>
    /// <remarks>
    /// Redundant with <c>length(Content)</c> and worth it: the projection rule for this table is
    /// that <see cref="Content"/> is never selected outside the one endpoint that serves bytes.
    /// </remarks>
    public int ByteCount { get; set; }

    /// <summary>When it was uploaded. The hook retention and sweeping will hang off later.</summary>
    public DateTimeOffset CreatedUtc { get; set; }
}
