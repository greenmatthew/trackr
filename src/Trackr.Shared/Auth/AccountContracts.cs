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
