using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Trackr.Shared.Auth;

/// <summary>Whether an account can be created right now, and on what terms.</summary>
/// <remarks>
/// CLAUDE.md section 8.4 rules out open public sign-up. There is exactly one moment when
/// anyone may register unconditionally - the first account on an empty database - and
/// after that an invite token is required.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RegistrationMode>))]
public enum RegistrationMode
{
    /// <summary>No accounts exist yet; the next caller claims the server.</summary>
    Bootstrap,

    /// <summary>Registration is closed except to holders of a valid invite token.</summary>
    InviteRequired
}

/// <param name="Mode">Which of the two registration paths is currently open.</param>
public sealed record RegistrationStatusResponse(RegistrationMode Mode);

public sealed class RegisterRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(256, MinimumLength = 12, ErrorMessage = "Use at least 12 characters.")]
    public string Password { get; set; } = "";

    /// <summary>Required unless this is the first account on an empty database.</summary>
    public string? InviteToken { get; set; }
}
