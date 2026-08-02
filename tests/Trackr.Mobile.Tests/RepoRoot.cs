namespace Trackr.Mobile.Tests;

/// <summary>
/// Locates the repository root, so a test can read a tracked file by its repository path
/// rather than guessing how deep under bin/ the test assembly ended up.
/// </summary>
/// <remarks>
/// Deliberately a copy of the one in Trackr.Docs.Tests rather than a shared helper. The two
/// test projects have no reference between them and sharing twenty lines would mean a third
/// project on the build graph, which costs more than the duplication does.
/// <para>
/// Walking up to a marker file rather than counting "../../.." because the number of levels
/// depends on the target framework and configuration in the output path.
/// </para>
/// </remarks>
internal static class RepoRoot
{
    private const string Marker = "Trackr.slnx";

    private static readonly Lazy<string> Located = new(Find);

    public static string Path => Located.Value;

    /// <summary>Enumerates tracked files matching a glob, relative to the repository root.</summary>
    public static IEnumerable<string> Glob(string relativeDirectory, string pattern) =>
        Directory.EnumerateFiles(
                System.IO.Path.Combine(Path, relativeDirectory),
                pattern,
                SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal);

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No {Marker} found above {AppContext.BaseDirectory}. This test reads tracked " +
            "files, so it must run from inside a checkout.");
    }
}
