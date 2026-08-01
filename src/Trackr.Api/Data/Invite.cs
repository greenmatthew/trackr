using Trackr.Api.Identity;

namespace Trackr.Api.Data;

/// <summary>
/// A single-use registration token.
/// </summary>
/// <remarks>
/// CLAUDE.md section 8.4 rules out open public sign-up. Registration is therefore only
/// possible in two situations: the very first account on a fresh database, or with a
/// valid invite minted by someone already signed in. This entity is the second case.
/// </remarks>
public class Invite
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// Uppercase hex SHA-256 of the raw token. The raw value is shown exactly once, at
    /// creation, and never stored - the same reasoning as password hashing. A plain fast
    /// hash is enough here (unlike a password): the token is 256 bits of CSPRNG output,
    /// so there is no low-entropy guess for an attacker to grind against.
    /// </summary>
    public string TokenHash { get; set; } = "";

    /// <summary>
    /// The first 8 characters of the raw token, kept in the clear purely so a human can
    /// tell rows apart in the invite list. 8 of 43 base64url characters still leaves
    /// around 200 bits unguessable.
    /// </summary>
    public string TokenPrefix { get; set; } = "";

    /// <summary>Free text so the issuer remembers who an invite was for.</summary>
    public string? Note { get; set; }

    public Guid CreatedByUserId { get; set; }
    public TrackrUser? CreatedBy { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresUtc { get; set; }

    /// <summary>Set when the invite is redeemed. Non-null means it can never be used again.</summary>
    public DateTimeOffset? RedeemedUtc { get; set; }
    public Guid? RedeemedByUserId { get; set; }
    public TrackrUser? RedeemedBy { get; set; }

    /// <summary>
    /// Soft revoke rather than a delete, so the record of who invited whom survives.
    /// </summary>
    public DateTimeOffset? RevokedUtc { get; set; }
}
