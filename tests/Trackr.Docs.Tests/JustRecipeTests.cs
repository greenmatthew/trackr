using System.Text.RegularExpressions;

namespace Trackr.Docs.Tests;

/// <summary>
/// Every <c>just</c> command the documentation tells someone to run must exist.
/// </summary>
/// <remarks>
/// The second check CLAUDE.md section 0 describes. Section 9 words it as "every just recipe
/// the README mentions"; this covers CLAUDE.md and the wiki too, since a renamed recipe
/// misleads a reader of any of them equally - and every page passes today.
/// </remarks>
public sealed class JustRecipeTests
{
    /// <summary>
    /// A recipe name may carry a <c>module::</c> prefix. '*' is allowed into the match only so
    /// that a documented glob such as <c>just docs::*</c> can be recognised and skipped rather
    /// than truncated to the module name and then reported as missing.
    /// </summary>
    private static readonly Regex Invocation =
        new(@"\bjust\s+(?<recipe>[a-z_][a-z0-9_*:-]*)", RegexOptions.Compiled);

    public static TheoryData<string, string> DocumentedRecipes()
    {
        var data = new TheoryData<string, string>();

        foreach (var (file, recipe) in Mentions().Distinct().OrderBy(m => m, PairOrder))
        {
            data.Add(file, recipe);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentedRecipes))]
    public void Every_documented_recipe_exists(string file, string recipe)
    {
        Assert.True(
            JustRecipes.Names.Contains(recipe),
            $"{file} tells the reader to run `just {recipe}`, which no justfile defines. " +
            "Either the recipe was renamed and the page was not, or the page has a typo.");
    }

    /// <summary>
    /// Guards the guard: if the scan or the parse silently stopped matching, the theory above
    /// would pass with nothing in it.
    /// </summary>
    [Fact]
    public void The_documentation_and_the_justfiles_are_both_being_read()
    {
        Assert.NotEmpty(DocumentedRecipes());
        Assert.Contains("dev", JustRecipes.Names);
        Assert.Contains("docs::publish", JustRecipes.Names);
    }

    private static IEnumerable<(string File, string Recipe)> Mentions()
    {
        foreach (var file in DocumentationFiles())
        {
            var markdown = RepoRoot.ReadText(file);

            foreach (var code in MarkdownCode.Extract(markdown))
            {
                foreach (Match match in Invocation.Matches(code))
                {
                    var recipe = match.Groups["recipe"].Value.TrimEnd(':', '-');

                    // A documented glob (`just docs::*`) names a set, not a recipe, and a bare
                    // module name (`just docs`) lists that module rather than running anything.
                    if (recipe.Contains('*') || JustRecipes.ModuleNames.Contains(recipe))
                    {
                        continue;
                    }

                    yield return (file, recipe);
                }
            }
        }
    }

    private static IEnumerable<string> DocumentationFiles()
    {
        yield return "README.md";
        yield return "CLAUDE.md";

        foreach (var page in RepoRoot.Glob("wiki", "*.md"))
        {
            yield return $"wiki/{System.IO.Path.GetFileName(page)}";
        }
    }

    private static readonly IComparer<(string File, string Recipe)> PairOrder =
        Comparer<(string File, string Recipe)>.Create((a, b) =>
        {
            var byFile = string.CompareOrdinal(a.File, b.File);
            return byFile != 0 ? byFile : string.CompareOrdinal(a.Recipe, b.Recipe);
        });
}
