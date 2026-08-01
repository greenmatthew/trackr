using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Trackr.Shared.Auth;

[JsonConverter(typeof(JsonStringEnumConverter<InviteStatus>))]
public enum InviteStatus
{
    /// <summary>Unused, unexpired, not revoked - someone can still register with it.</summary>
    Active,
    Redeemed,
    Expired,
    Revoked
}

public sealed class CreateInviteRequest
{
    /// <summary>Free text so the issuer remembers who it was for.</summary>
    [StringLength(200)]
    public string? Note { get; set; }

    /// <summary>Lifetime in hours. Clamped server-side to between 1 hour and 30 days.</summary>
    [Range(1, 720)]
    public int ExpiresInHours { get; set; } = 168;
}

/// <param name="Token">
/// The raw token, returned exactly once. Only its hash is stored, so it cannot be
/// retrieved again - if it is lost, revoke the invite and mint another.
/// </param>
/// <param name="InviteUrl">A ready-to-share registration link containing the token.</param>
public sealed record InviteCreatedResponse(
    Guid Id,
    string Token,
    string InviteUrl,
    DateTimeOffset ExpiresUtc);

/// <param name="TokenPrefix">First 8 characters of the token, so rows are tellable apart.</param>
public sealed record InviteResponse(
    Guid Id,
    string TokenPrefix,
    string? Note,
    InviteStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ExpiresUtc,
    DateTimeOffset? RedeemedUtc,
    string? RedeemedByEmail);
