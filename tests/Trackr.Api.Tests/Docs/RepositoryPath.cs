namespace Trackr.Api.Tests.Docs;

/// <summary>
/// Locates the repository root, so the documentation tests can read tracked files by their
/// repository path.
/// </summary>
/// <remarks>
/// Walks up to the file that marks the root rather than counting levels out of bin/, whose depth
/// depends on the target framework and configuration - a change to either would otherwise break
/// every test here silently.
/// <para>
/// The same job as <c>Trackr.Docs.Tests.RepoRoot</c>, and separate from it on purpose: that
/// project deliberately references no source project, so anything comparing a page against real
/// code has to live over here instead.
/// </para>
/// </remarks>
internal static class RepositoryPath
{
    private const string Marker = "Trackr.slnx";

    private static readonly Lazy<string> Located = new(Find);

    public static string Root => Located.Value;

    /// <summary>Reads a tracked file, given its path relative to the repository root.</summary>
    public static string ReadText(string relativePath) =>
        File.ReadAllText(Path.Combine(Root, relativePath));

    public static string Of(string relativePath) => Path.Combine(Root, relativePath);

    private static string Find()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"No {Marker} found above {AppContext.BaseDirectory}. These tests read tracked files, "
                + "so they must run from inside a checkout.");
    }
}
