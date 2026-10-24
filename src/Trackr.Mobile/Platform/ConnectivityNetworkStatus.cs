using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Android's view of whether this phone has a network.
/// </summary>
/// <remarks>
/// <b><see cref="NetworkAccess.Local"/> counts as connected here, and that is not an oversight.</b>
/// Trackr's server is somebody's home server, and CLAUDE.md section 8 recommends keeping it off the
/// public internet entirely - so "this phone can reach the local network but not the internet" is
/// the configuration the app is designed for rather than a degraded one. Treating it as offline
/// would queue every meal on the one setup the project recommends.
/// <para>
/// Everything except a flat "no network" counts as connected, and even that answer only ever
/// <em>starts</em> a delivery rather than preventing one - see <see cref="INetworkStatus"/>. A wrong
/// "connected" costs one failed request; a wrong "disconnected" would queue a meal that could have
/// gone straight through, which the emulator does report in airplane mode.
/// </para>
/// </remarks>
public sealed class ConnectivityNetworkStatus : INetworkStatus, IDisposable
{
    public ConnectivityNetworkStatus() =>
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

    public event Action? Changed;

    public bool IsConnected => Connectivity.Current.NetworkAccess is not NetworkAccess.None;

    public void Dispose() =>
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        Changed?.Invoke();
}
