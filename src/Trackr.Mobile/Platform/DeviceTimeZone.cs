using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Platform;

/// <summary>
/// The phone's zone, as Android names it.
/// </summary>
/// <remarks>
/// Android's zone ids are IANA ids already - "Europe/London" - so no translation is needed here.
/// Windows would need one, which is a reason to keep this behind the interface rather than a reason
/// to write one now.
/// </remarks>
public sealed class DeviceTimeZone : IDeviceTimeZone
{
    public string? IanaId => TimeZoneInfo.Local.Id is { Length: > 0 } id && id != "Local" ? id : null;
}
