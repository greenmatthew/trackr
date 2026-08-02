using Trackr.Api.Identity;

namespace Trackr.Api.Data;

/// <summary>
/// An account's profile picture.
/// </summary>
/// <remarks>
/// A table of its own rather than a column on <see cref="TrackrUser"/>, because the bytes are
/// large and almost never wanted: every sign-in, every token refresh and every authorization
/// check loads a user row, and none of them want an image. The timestamp that says *whether*
/// there is one lives on the user instead - see <see cref="TrackrUser.AvatarUpdatedUtc"/> -
/// so the common question is answered without touching this table at all.
/// <para>
/// Bytes in Postgres rather than a file on a volume, deliberately. A self-hoster who follows
/// wiki/Backup-and-Restore.md ends up with a database dump; a second storage location is a
/// second thing to remember to back up, and the one people forget. Profile pictures are small
/// and few - one per account, on a household-sized install.
/// </para>
/// </remarks>
public class UserAvatar
{
    /// <summary>Primary key and foreign key both: one avatar per account, at most.</summary>
    public Guid UserId { get; set; }

    public TrackrUser? User { get; set; }

    public byte[] Content { get; set; } = [];

    /// <summary>Echoed back on GET, so the client renders what was actually stored.</summary>
    public string ContentType { get; set; } = "";

    /// <summary>
    /// Mirrors <see cref="TrackrUser.AvatarUpdatedUtc"/>, and is what the ETag is derived
    /// from. Kept here too so this row is self-describing.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; set; }
}
