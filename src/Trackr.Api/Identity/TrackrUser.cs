using Microsoft.AspNetCore.Identity;

namespace Trackr.Api.Identity;

/// <summary>
/// An account. Everything about credentials, 2FA and lockout comes from
/// <see cref="IdentityUser{TKey}"/> - CLAUDE.md section 10 is explicit that we do not
/// hand-roll any of it.
/// </summary>
/// <remarks>
/// The key is a <see cref="Guid"/> rather than Identity's default string because every
/// table from milestone 3 onwards (food catalog, log entries, nutrient snapshots) gets
/// an owner foreign key, and Postgres stores a uuid in 16 bytes against 37 for the text
/// form. This choice is effectively permanent once the first migration is applied.
/// </remarks>
public class TrackrUser : IdentityUser<Guid>
{
    public TrackrUser()
    {
        // The string-keyed IdentityUser assigns its own Id in the constructor, but the
        // generic IdentityUser<TKey> cannot - it has no way to make an arbitrary TKey.
        // Without this line a new user is saved with Guid.Empty, and the second one
        // fails on the primary key. Version 7 GUIDs are time-ordered, so the primary
        // key index keeps appending rather than fragmenting on random inserts.
        Id = Guid.CreateVersion7();
    }

    /// <summary>When the account was created. Identity itself does not track this.</summary>
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
