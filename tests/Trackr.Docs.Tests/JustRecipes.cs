using System.Text.RegularExpressions;

namespace Trackr.Docs.Tests;

/// <summary>
/// The set of recipes the task runner actually defines, read out of the justfiles.
/// </summary>
/// <remarks>
/// Parsed rather than obtained from `just --list`, so the suite does not need `just` on PATH.
/// The grammar being matched is small and stable: a recipe is a line starting in column zero
/// whose name is followed by optional parameters, a colon, and optional dependencies.
/// </remarks>
internal static class JustRecipes
{
    private const string RootJustfile = "Justfile";

    /// <summary>Module declaration: <c>mod server 'just/server.just'</c>.</summary>
    private static readonly Regex Module =
        new(@"^mod\s+(?<name>[a-z][a-z0-9_-]*)\s+'(?<path>[^']+)'", RegexOptions.Multiline);

    /// <summary>
    /// A recipe header. The negative lookahead on '=' is what excludes settings and exports -
    /// <c>set working-directory := '..'</c> would otherwise read as a recipe named "set".
    /// </summary>
    private static readonly Regex Recipe =
        new(@"^(?<name>[a-z_][a-z0-9_-]*)(?<params>[^:\r\n]*):(?!=)", RegexOptions.Multiline);

    private static readonly Lazy<IReadOnlySet<string>> All = new(Load);
    private static readonly Lazy<IReadOnlySet<string>> Modules = new(LoadModuleNames);

    /// <summary>
    /// Every invocable name: bare recipes from the root justfile, and <c>module::recipe</c>
    /// for each module's own.
    /// </summary>
    public static IReadOnlySet<string> Names => All.Value;

    /// <summary>Module names on their own, which `just &lt;module&gt;` lists rather than runs.</summary>
    public static IReadOnlySet<string> ModuleNames => Modules.Value;

    private static IReadOnlySet<string> LoadModuleNames() =>
        Module.Matches(RepoRoot.ReadText(RootJustfile))
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> Load()
    {
        var root = RepoRoot.ReadText(RootJustfile);
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in RecipeNamesIn(root))
        {
            names.Add(name);
        }

        foreach (Match module in Module.Matches(root))
        {
            var prefix = module.Groups["name"].Value;
            var contents = RepoRoot.ReadText(module.Groups["path"].Value);

            foreach (var name in RecipeNamesIn(contents))
            {
                names.Add($"{prefix}::{name}");
            }
        }

        return names;
    }

    private static IEnumerable<string> RecipeNamesIn(string justfile) =>
        Recipe.Matches(justfile).Select(m => m.Groups["name"].Value);
}
