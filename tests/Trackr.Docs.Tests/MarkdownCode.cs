using System.Text.RegularExpressions;

namespace Trackr.Docs.Tests;

/// <summary>
/// Pulls the code out of a markdown document - fenced blocks and inline spans.
/// </summary>
/// <remarks>
/// Needed because "just" is an ordinary English word. CLAUDE.md contains "just a", "just goes"
/// and "just macros" in prose; only the backticked occurrences are commands. Scanning the raw
/// text would report half a dozen recipes that were never meant to be recipes.
/// </remarks>
internal static class MarkdownCode
{
    private static readonly Regex FencedBlock =
        new(@"^```[^\n]*\n(?<code>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline);

    // No newline inside an inline span, which is what keeps a stray backtick in prose from
    // swallowing the rest of the paragraph.
    private static readonly Regex InlineSpan = new(@"`(?<code>[^`\n]+)`");

    /// <summary>Every fenced block and inline span in the document, in no particular order.</summary>
    public static IEnumerable<string> Extract(string markdown)
    {
        var fences = FencedBlock.Matches(markdown);

        foreach (Match fence in fences)
        {
            yield return fence.Groups["code"].Value;
        }

        // Inline spans are searched over the text with the fenced blocks removed, so the
        // backticks that delimit a fence cannot pair up with each other across it.
        var withoutFences = FencedBlock.Replace(markdown, string.Empty);

        foreach (Match span in InlineSpan.Matches(withoutFences))
        {
            yield return span.Groups["code"].Value;
        }
    }
}
