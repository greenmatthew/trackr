using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Trackr.Shared.Auth;

/// <remarks>
/// Requests are mutable classes rather than positional records throughout this namespace.
/// Blazor's <c>InputText @bind-Value</c> needs a settable property, so a record with
/// init-only accessors silently fails to bind inside an EditForm.
/// </remarks>
public sealed class LoginRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    /// <summary>Keep the session across browser restarts rather than ending it with the tab.</summary>
    public bool RememberMe { get; set; }
}

/// <summary>The second step of a login, once the password step reported RequiresTwoFactor.</summary>
public sealed class TwoFactorLoginRequest
{
    [Required]
    [StringLength(8, MinimumLength = 6)]
    public string Code { get; set; } = "";

    public bool RememberMe { get; set; }

    /// <summary>Skip the 2FA step on this browser next time.</summary>
    public bool RememberMachine { get; set; }
}

/// <summary>Login using one of the single-use codes issued when 2FA was enabled.</summary>
public sealed class RecoveryCodeLoginRequest
{
    [Required]
    public string RecoveryCode { get; set; } = "";
}

public sealed class ForgotPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";
}

public sealed class ResetPasswordRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Code { get; set; } = "";

    [Required]
    [StringLength(256, MinimumLength = 12, ErrorMessage = "Use at least 12 characters.")]
    public string NewPassword { get; set; } = "";
}

/// <remarks>
/// Serialised as a string rather than an integer. The converter is declared on the type
/// so both the API and the client agree without either side configuring it.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<LoginStatus>))]
public enum LoginStatus
{
    /// <summary>Signed in. The session cookie is set.</summary>
    Succeeded,

    /// <summary>Password accepted; the caller now owes a 2FA code.</summary>
    RequiresTwoFactor,

    /// <summary>Too many failed attempts. See <see cref="LoginResponse.LockoutEndUtc"/>.</summary>
    LockedOut,

    /// <summary>The 2FA step was attempted without a live password step, or it timed out.</summary>
    ChallengeExpired,

    /// <summary>Wrong credentials. Deliberately does not distinguish unknown user from bad password.</summary>
    Failed
}

/// <param name="Status">What happened, and therefore what the client should do next.</param>
/// <param name="LockoutEndUtc">When the account unlocks. Only set for <see cref="LoginStatus.LockedOut"/>.</param>
public sealed record LoginResponse(
    LoginStatus Status,
    DateTimeOffset? LockoutEndUtc = null);

/// <summary>Result of <c>GET /api/auth/me</c> - who the session cookie belongs to.</summary>
/// <param name="UserId">The account's identifier.</param>
/// <param name="Email">The account's email address, which is also its user name.</param>
/// <param name="TwoFactorEnabled">Whether the account has an authenticator app enrolled.</param>
/// <param name="AvatarUpdatedUtc">
/// When the profile picture last changed, or null if the account has none. Not the picture
/// itself: this is the marker a client compares against its cached copy to decide whether to
/// re-fetch <c>GET /api/account/avatar</c>. Optional so that adding it did not break every
/// existing positional construction of this record.
/// </param>
public sealed record MeResponse(
    Guid UserId,
    string Email,
    bool TwoFactorEnabled,
    DateTimeOffset? AvatarUpdatedUtc = null);
