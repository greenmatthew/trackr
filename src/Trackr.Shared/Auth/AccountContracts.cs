using System.ComponentModel.DataAnnotations;

namespace Trackr.Shared.Auth;

public sealed class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = "";

    [Required]
    [StringLength(256, MinimumLength = 12, ErrorMessage = "Use at least 12 characters.")]
    public string NewPassword { get; set; } = "";
}

/// <summary>
/// Asking for the account's email address to be changed.
/// </summary>
/// <remarks>
/// <strong>Nothing changes until a link sent to the new address is followed.</strong> The email
/// address is the login and the only route back in through a password reset, so applying a typo
/// immediately would lock somebody out of their own server with no way back. The password is
/// re-checked because a session left open on a shared laptop should not be enough to take an
/// account over.
/// </remarks>
public sealed class ChangeEmailRequest
{
    [Required]
    public string Password { get; set; } = "";

    [Required]
    [EmailAddress]
    public string NewEmail { get; set; } = "";
}

/// <summary>Following the link, which is what actually changes the address.</summary>
public sealed class ConfirmEmailChangeRequest
{
    [Required]
    [EmailAddress]
    public string NewEmail { get; set; } = "";

    [Required]
    public string Code { get; set; } = "";
}

/// <summary>
/// Deleting the account and everything in it.
/// </summary>
/// <remarks>
/// The password is re-checked for the reason it is on an email change, and more so: this is not
/// undoable. <see cref="Confirmation"/> has to be typed, so the request cannot be made by a stray
/// tap on a button.
/// </remarks>
public sealed class DeleteAccountRequest
{
    [Required]
    public string Password { get; set; } = "";

    /// <summary>Must equal <see cref="RequiredConfirmation"/>, typed by hand.</summary>
    [Required]
    public string Confirmation { get; set; } = "";

    /// <summary>The word the client must have the user type. Compared case-insensitively.</summary>
    public const string RequiredConfirmation = "DELETE";
}

/// <summary>Current 2FA state for the signed-in account.</summary>
/// <param name="IsEnabled">Whether a login actually demands a code.</param>
/// <param name="HasAuthenticatorKey">Whether a shared secret exists (enrolment may be half-finished).</param>
/// <param name="RecoveryCodesLeft">How many single-use codes remain unspent.</param>
/// <param name="IsMachineRemembered">Whether this browser is currently skipping the 2FA step.</param>
public sealed record TwoFactorStatusResponse(
    bool IsEnabled,
    bool HasAuthenticatorKey,
    int RecoveryCodesLeft,
    bool IsMachineRemembered);

/// <summary>Everything needed to enrol an authenticator app.</summary>
/// <param name="SharedKey">The secret in 4-character groups, for typing in by hand.</param>
/// <param name="AuthenticatorUri">The otpauth:// URI the QR code encodes.</param>
/// <param name="QrCodeSvgDataUri">
/// The same URI as a scannable SVG, ready for an img src. A data URI rather than raw
/// markup so the client never has to render untrusted HTML.
/// </param>
public sealed record TwoFactorEnrollmentResponse(
    string SharedKey,
    string AuthenticatorUri,
    string QrCodeSvgDataUri);

public sealed class TwoFactorCodeRequest
{
    [Required]
    [StringLength(8, MinimumLength = 6)]
    public string Code { get; set; } = "";
}

/// <summary>Disabling 2FA re-checks the password, since it lowers the account's protection.</summary>
public sealed class DisableTwoFactorRequest
{
    [Required]
    public string Password { get; set; } = "";
}

/// <param name="RecoveryCodes">
/// Single-use codes for getting in without the authenticator app. Returned exactly once -
/// the server keeps only hashes, so they cannot be shown again.
/// </param>
public sealed record RecoveryCodesResponse(IReadOnlyList<string> RecoveryCodes);
