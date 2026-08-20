namespace Trackr.Mobile.Core.Platform;

/// <summary>
/// The zone this phone is in.
/// </summary>
/// <remarks>
/// A suggestion only. The account's zone is stored on the server because the server aggregates -
/// CLAUDE.md section 9.13 - and this exists so the user is offered the right answer rather than
/// asked to type "Europe/London" from memory.
/// <para>
/// Behind an interface because <c>TimeZoneInfo.Local</c> answers differently on a test host than on
/// a device, and a view model that reads it directly can only be tested wherever it happens to run.
/// </para>
/// </remarks>
public interface IDeviceTimeZone
{
    /// <summary>The IANA id, or null if the platform will not name one.</summary>
    string? IanaId { get; }
}
