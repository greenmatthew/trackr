namespace Trackr.Mobile.Core.Platform;

/// <summary>
/// Where this install's Trackr server lives.
/// </summary>
/// <remarks>
/// The web app never needs this: nginx serves it and proxies <c>/api/</c>, so it uses
/// relative URLs and is structurally incapable of pointing at the wrong server. A
/// self-hosted app has no such luxury - every user's server is at a different address, so
/// the first thing the app must do is ask. This is the same first-run flow Immich's app has.
/// </remarks>
public interface IServerSettings
{
    /// <summary>The configured server, or null before first-run setup has completed.</summary>
    Uri? BaseUrl { get; }

    Task SetBaseUrlAsync(Uri baseUrl);

    Task ClearAsync();
}
