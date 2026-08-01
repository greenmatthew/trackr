using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Remembers which server this install talks to.
/// </summary>
/// <remarks>
/// <c>Preferences</c> rather than <c>SecureStorage</c>: the server address is not a secret,
/// and it is read on nearly every request, so it is worth having synchronously in memory
/// rather than behind an async keystore call.
/// </remarks>
public sealed class PreferencesServerSettings : IServerSettings
{
    private const string BaseUrlKey = "trackr.server_url";

    private Uri? _cached;

    public PreferencesServerSettings()
    {
        var stored = Preferences.Get(BaseUrlKey, defaultValue: null);

        if (!string.IsNullOrEmpty(stored) && Uri.TryCreate(stored, UriKind.Absolute, out var parsed))
        {
            _cached = parsed;
        }
    }

    public Uri? BaseUrl => _cached;

    public Task SetBaseUrlAsync(Uri baseUrl)
    {
        _cached = baseUrl;
        Preferences.Set(BaseUrlKey, baseUrl.ToString());

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _cached = null;
        Preferences.Remove(BaseUrlKey);

        return Task.CompletedTask;
    }
}
