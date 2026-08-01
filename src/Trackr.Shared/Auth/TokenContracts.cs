using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Trackr.Shared.Auth;

/// <summary>
/// Sign-in for a native client, which cannot usefully hold the session cookie the web app
/// uses.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>one</b> endpoint that takes an optional second factor, rather than
/// mirroring the web's two-step <c>/api/auth/login</c> then <c>/api/auth/login/2fa</c>.
/// That handshake is carried by Identity's short-lived <c>TwoFactorUserId</c> <i>cookie</i>,
/// which a native <c>HttpClient</c> has no good way to hold across two calls.
/// </para>
/// <para>
/// So the app posts credentials, may get <see cref="LoginStatus.RequiresTwoFactor"/> back,
/// prompts for the code, and posts the whole thing again including the code. The password is
/// re-checked on the second call, which is why there is no window to expire.
/// </para>
/// </remarks>
public sealed class TokenRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    public string Password { get; set; } = "";

    /// <summary>
    /// A current code from the authenticator app. Supplied only on the second attempt,
    /// after the first reported <see cref="LoginStatus.RequiresTwoFactor"/>.
    /// </summary>
    public string? TwoFactorCode { get; set; }

    /// <summary>
    /// One of the single-use codes issued when 2FA was enabled. An alternative to
    /// <see cref="TwoFactorCode"/> for someone who has lost their authenticator.
    /// </summary>
    public string? TwoFactorRecoveryCode { get; set; }
}

/// <summary>Exchange a refresh token for a new access token.</summary>
public sealed class RefreshRequest
{
    [Required]
    public string RefreshToken { get; set; } = "";
}

/// <summary>
/// A successful token sign-in.
/// </summary>
/// <remarks>
/// Shaped like Identity's own <c>AccessTokenResponse</c> so the wire format matches what
/// <c>AddBearerToken</c> produces, but declared here so the mobile client can deserialise it
/// without taking a dependency on ASP.NET Core.
/// </remarks>
/// <param name="TokenType">Always <c>Bearer</c>.</param>
/// <param name="AccessToken">Send as <c>Authorization: Bearer {token}</c>.</param>
/// <param name="ExpiresIn">Lifetime of the access token, in seconds.</param>
/// <param name="RefreshToken">Used to get a new access token without re-entering the password.</param>
public sealed record TokenResponse(
    [property: JsonPropertyName("tokenType")] string TokenType,
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("expiresIn")] long ExpiresIn,
    [property: JsonPropertyName("refreshToken")] string RefreshToken);

/// <summary>
/// Why a token sign-in did <b>not</b> succeed.
/// </summary>
/// <remarks>
/// Only sent with a 401. A success is a 200 carrying a <see cref="TokenResponse"/>, because
/// Identity's bearer handler writes that body itself when the sign-in completes - there is
/// no opportunity to wrap it in an envelope. So the client branches on the status code
/// first, exactly as the web client already does for <c>/api/auth/login</c>.
///
/// Reuses <see cref="LoginStatus"/> so both clients interpret the same values.
/// </remarks>
/// <param name="Status">What happened, and therefore what the client should do next.</param>
/// <param name="LockoutEndUtc">When the account unlocks. Only set for <see cref="LoginStatus.LockedOut"/>.</param>
public sealed record TokenLoginResponse(
    LoginStatus Status,
    DateTimeOffset? LockoutEndUtc = null);
