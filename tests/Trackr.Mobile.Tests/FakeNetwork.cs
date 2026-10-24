using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Tests;

/// <summary>
/// A network the test controls, standing in for Android's connectivity.
/// </summary>
/// <remarks>
/// Connected by default, so every test written before there was such a thing as an offline queue
/// still describes a phone with a signal.
/// </remarks>
internal sealed class FakeNetwork : INetworkStatus
{
    public bool IsConnected { get; private set; } = true;

    public event Action? Changed;

    public void GoOffline() => Set(false);

    /// <summary>What a phone coming out of a tunnel does. Raises the event the queue listens to.</summary>
    public void ComeBack() => Set(true);

    /// <summary>
    /// Connected again, with no announcement.
    /// </summary>
    /// <remarks>
    /// A real occurrence rather than a testing convenience: Android raises nothing for a change
    /// that happened while the app was not running, so the queue has to survive learning about a
    /// network only when something else asks. It also lets a test drive the flushes itself.
    /// </remarks>
    public void ComeBackUnnoticed() => IsConnected = true;

    private void Set(bool connected)
    {
        IsConnected = connected;

        Changed?.Invoke();
    }
}
