namespace Trackr.Docs.Tests;

/// <summary>
/// Locates the repository root, so these tests can read tracked files by their repository
/// path rather than guessing how deep under bin/ the test assembly ended up.
/// </summary>
/// <remarks>
/// Walking up to a marker file rather than counting "../../.." because the number of levels
/// depends on the target framework and configuration in the output path, and a change to
/// either would silently break every test here.
/// </remarks>
internal static class RepoRoot
{
    private const string Marker = "Trackr.slnx";

    private static readonly Lazy<string> Located = new(Find);

    public static string Path => Located.Value;

    /// <summary>Reads a tracked file, given its path relative to the repository root.</summary>
    public static string ReadText(string relativePath) =>
        File.ReadAllText(System.IO.Path.Combine(Path, relativePath));

    /// <summary>Enumerates tracked files matching a glob, relative to the repository root.</summary>
    public static IEnumerable<string> Glob(string relativeDirectory, string pattern) =>
        Directory.EnumerateFiles(System.IO.Path.Combine(Path, relativeDirectory), pattern)
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
            $"No {Marker} found above {AppContext.BaseDirectory}. These tests read tracked " +
            "files, so they must run from inside a checkout.");
    }
}
