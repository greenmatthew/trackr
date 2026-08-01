namespace Trackr.Mobile.Core.Platform;

/// <summary>
/// Persists the bearer tokens between app launches.
/// </summary>
/// <remarks>
/// Implemented on Android over <c>SecureStorage</c>, which is backed by the Android Keystore.
/// It is an interface here for two reasons: <c>SecureStorage</c> is a MAUI type and this
/// project deliberately does not reference MAUI, and tests need to substitute it.
/// <para>
/// Note the asymmetry with the web app, which stores nothing at all - its session is an
/// HttpOnly cookie the browser holds and JavaScript cannot read. Holding a token in app
/// storage is strictly weaker, and is accepted because a native client has no better
/// option. Keystore-backed storage is what keeps it reasonable.
/// </para>
/// </remarks>
public interface ITokenStore
{
    Task<StoredTokens?> ReadAsync();

    Task WriteAsync(StoredTokens tokens);

    Task ClearAsync();
}

/// <param name="AccessToken">Sent as <c>Authorization: Bearer</c>.</param>
/// <param name="RefreshToken">Exchanged for a new access token when the current one expires.</param>
/// <param name="AccessTokenExpiresUtc">
/// When the access token stops working. Stored so the app can refresh proactively rather
/// than discovering the expiry through a failed request.
/// </param>
public sealed record StoredTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresUtc);
