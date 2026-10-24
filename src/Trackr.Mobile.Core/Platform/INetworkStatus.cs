namespace Trackr.Mobile.Core.Platform;

/// <summary>
/// Whether this phone currently has a network at all.
/// </summary>
/// <remarks>
/// <b>A trigger, not a verdict, and nothing decides anything on its word.</b> It is wrong in both
/// directions: Android reports a connection the moment one is associated, which includes a
/// captive-portal wifi that answers nothing - and it reports <em>none</em> in airplane mode on an
/// emulator whose host server is still perfectly reachable, which is how this came to be documented
/// this firmly. A save is therefore always attempted, and always able to fail.
/// <para>
/// What it buys is the other direction: knowing a connection has <em>returned</em> is what turns
/// the offline queue from something the user has to remember into something that empties itself.
/// </para>
/// <para>
/// Behind an interface because <c>Connectivity.Current</c> is MAUI's and needs a device, and
/// because a test of the queue has to be able to say "the network came back" without one.
/// </para>
/// </remarks>
public interface INetworkStatus
{
    /// <summary>False only when the platform is sure there is no network.</summary>
    bool IsConnected { get; }

    /// <summary>Raised when the answer above changes.</summary>
    event Action? Changed;
}
