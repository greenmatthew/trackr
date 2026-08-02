namespace Trackr.Mobile.Core.Platform;

/// <summary>
/// Where the local SQLite database lives on this device.
/// </summary>
/// <remarks>
/// A one-property abstraction because the answer is <c>FileSystem.AppDataDirectory</c>, which
/// is a MAUI type, and this project deliberately references none. It also lets the tests hand
/// back <c>:memory:</c> and exercise the real store without touching a disk.
/// </remarks>
public interface ILocalStorePath
{
    /// <summary>
    /// The SQLite data source: a file path on a device, or <c>:memory:</c> in a test.
    /// </summary>
    string DataSource { get; }
}
