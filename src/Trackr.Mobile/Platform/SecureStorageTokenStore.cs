using System.Globalization;
using Microsoft.Extensions.Logging;
using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Keeps the bearer tokens in Android's keystore-backed secure storage.
/// </summary>
/// <remarks>
/// MAUI's <c>SecureStorage</c> maps to <c>EncryptedSharedPreferences</c> on Android, with
/// the key held in the hardware-backed Android Keystore. That is what makes storing a token
/// on the device acceptable at all - see <see cref="ITokenStore"/> for why the web app,
/// which stores nothing, has the stronger position.
/// </remarks>
public sealed class SecureStorageTokenStore(ILogger<SecureStorageTokenStore> logger) : ITokenStore
{
    private const string AccessTokenKey = "trackr.access_token";
    private const string RefreshTokenKey = "trackr.refresh_token";
    private const string ExpiresKey = "trackr.access_token_expires";

    public async Task<StoredTokens?> ReadAsync()
    {
        try
        {
            var access = await SecureStorage.GetAsync(AccessTokenKey);
            var refresh = await SecureStorage.GetAsync(RefreshTokenKey);
            var expires = await SecureStorage.GetAsync(ExpiresKey);

            if (string.IsNullOrEmpty(access) || string.IsNullOrEmpty(refresh))
            {
                return null;
            }

            // A missing or unparseable expiry is treated as "expired" rather than throwing:
            // the refresh path then runs and either renews the session or ends it cleanly.
            var expiresUtc = DateTimeOffset.TryParse(
                expires,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : DateTimeOffset.MinValue;

            return new StoredTokens(access, refresh, expiresUtc);
        }
        catch (Exception ex)
        {
            // SecureStorage throws if the keystore entry cannot be decrypted, which happens
            // after some OS upgrades and after a backup restore onto a different device.
            // Treating it as "signed out" costs one login; letting it propagate would crash
            // the app on launch, every launch.
            logger.LogWarning(ex, "Could not read stored tokens; treating as signed out.");
            SecureStorage.RemoveAll();

            return null;
        }
    }

    public async Task WriteAsync(StoredTokens tokens)
    {
        await SecureStorage.SetAsync(AccessTokenKey, tokens.AccessToken);
        await SecureStorage.SetAsync(RefreshTokenKey, tokens.RefreshToken);
        await SecureStorage.SetAsync(
            ExpiresKey,
            tokens.AccessTokenExpiresUtc.ToString("O", CultureInfo.InvariantCulture));
    }

    public Task ClearAsync()
    {
        SecureStorage.Remove(AccessTokenKey);
        SecureStorage.Remove(RefreshTokenKey);
        SecureStorage.Remove(ExpiresKey);

        return Task.CompletedTask;
    }
}
