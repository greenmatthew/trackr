using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Platform;

/// <summary>
/// Puts the local database in the app's private data directory.
/// </summary>
/// <remarks>
/// <c>FileSystem.AppDataDirectory</c> is per-app private storage that Android removes with the
/// app. It is not backed up either: <c>allowBackup</c> is false and
/// <c>data_extraction_rules.xml</c> excludes the <c>database</c> domain from both cloud backup
/// and device transfer, which was written in anticipation of exactly this file - see the
/// comment in <c>AndroidManifest.xml</c>.
/// </remarks>
public sealed class AppDataLocalStorePath : ILocalStorePath
{
    public string DataSource { get; } = Path.Combine(FileSystem.AppDataDirectory, "trackr.db");
}
