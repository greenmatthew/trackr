using System.Reflection;
using System.Text.Json.Serialization;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests.Docs;

/// <summary>
/// Keeps <c>wiki/Nutrient-Reference.md</c> in step with the nutrients the server actually seeds.
/// </summary>
/// <remarks>
/// CLAUDE.md section 0: documentation is kept honest by tests, not by discipline. That page is the
/// only place a self-hoster can learn which keys and units exist, so a nutrient added to the code
/// and not to the page is invisible - and one removed from the code but left on the page is worse,
/// because somebody will try to use it.
/// <para>
/// This asserts agreement rather than generating the page, unlike <see cref="ApiReferenceTests"/>.
/// Most of that page is design rules in prose that no generator could write; only the tables are
/// mechanical, so only the tables are checked.
/// </para>
/// <para>
/// No fixture and no Docker: it reads a file and a static list.
/// </para>
/// </remarks>
public sealed class NutrientReferenceTests
{
    private const string Page = "wiki/Nutrient-Reference.md";

    public static TheoryData<string> SeededKeys()
    {
        var data = new TheoryData<string>();

        foreach (var definition in NutrientSeed.All)
        {
            data.Add(definition.Key);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SeededKeys))]
    public void Every_seeded_nutrient_is_documented(string key)
    {
        var definition = NutrientSeed.All.Single(nutrient => nutrient.Key == key);
        var rows = DocumentedRows();

        Assert.True(
            rows.TryGetValue(key, out var row),
            $"{Page} does not mention the nutrient '{key}'. Every seeded nutrient needs a row - "
                + "that page is the only place the keys and units are written down.");

        Assert.Equal(definition.DisplayName, row!.DisplayName);
        Assert.Equal(SymbolOf(definition.Unit), row.Unit);
        Assert.Equal(definition.SortOrder, row.SortOrder);
        Assert.Equal(definition.Group, row.Group);
    }

    [Fact]
    public void The_page_documents_nothing_the_server_does_not_seed()
    {
        var seeded = NutrientSeed.All.Select(nutrient => nutrient.Key).ToHashSet(StringComparer.Ordinal);

        var strays = DocumentedRows().Keys.Where(key => !seeded.Contains(key)).ToArray();

        Assert.True(
            strays.Length == 0,
            $"{Page} documents {string.Join(", ", strays)}, which the server does not seed. A "
                + "nutrient that only exists on the page is a key nobody can actually record.");
    }

    /// <remarks>
    /// Within a section only. The page is grouped the way a label is, and a label interleaves the
    /// always-present four with their own breakdowns, so the orders deliberately jump between
    /// sections - "Always present" runs 10, 20, 90, 130. What must never happen is two nutrients
    /// claiming one position, which would leave their rendering order undefined.
    /// </remarks>
    [Fact]
    public void The_documented_orders_are_unique_and_ascend_within_each_section()
    {
        var rows = DocumentedRows().Values.ToArray();
        var orders = rows.Select(row => row.SortOrder).ToArray();

        Assert.Equal(orders.Length, orders.Distinct().Count());

        foreach (var section in rows.GroupBy(row => row.Group))
        {
            var within = section.Select(row => row.SortOrder).ToArray();

            Assert.Equal(within.OrderBy(order => order), within);
        }
    }

    /// <summary>
    /// Reads every four-column nutrient table on the page, keyed by nutrient key and in the order
    /// they appear.
    /// </summary>
    /// <remarks>
    /// The section heading a table sits under is its group, which is why the headings are asserted
    /// rather than just the rows: "Vitamin C" appearing under Minerals would be wrong in a way no
    /// row-level check would notice.
    /// </remarks>
    private static Dictionary<string, DocumentedNutrient> DocumentedRows()
    {
        var rows = new Dictionary<string, DocumentedNutrient>(StringComparer.Ordinal);
        NutrientGroup? group = null;

        foreach (var raw in RepositoryPath.ReadText(Page).Split('\n'))
        {
            var line = raw.Trim();

            if (line.StartsWith('#'))
            {
                group = GroupFor(line.TrimStart('#').Trim());
                continue;
            }

            if (!line.StartsWith('|') || group is null)
            {
                continue;
            }

            var cells = line.Trim('|').Split('|').Select(cell => cell.Trim().Trim('`')).ToArray();

            // Skips the header and the --- separator, and any table that is not this shape.
            if (cells.Length != 4 || !int.TryParse(cells[3], out var sortOrder))
            {
                continue;
            }

            rows[cells[0]] = new DocumentedNutrient(cells[1], cells[2], sortOrder, group.Value);
        }

        Assert.True(rows.Count > 0, $"No nutrient tables were found in {Page}.");

        return rows;
    }

    /// <summary>Maps a section heading to the group it stands for.</summary>
    private static NutrientGroup? GroupFor(string heading) => heading switch
    {
        "Always present" => NutrientGroup.Core,
        "Fat breakdown" => NutrientGroup.FatBreakdown,
        "Carbohydrate breakdown" => NutrientGroup.CarbohydrateBreakdown,
        "Sterols and electrolytes" => NutrientGroup.SterolsAndElectrolytes,
        "Vitamins" => NutrientGroup.Vitamins,
        "Minerals" => NutrientGroup.Minerals,

        // Any other heading - the design rules, the display notes - ends the current table.
        _ => null
    };

    /// <summary>
    /// The unit as it is written on the page, read from the enum's own JSON name.
    /// </summary>
    /// <remarks>
    /// Reflection rather than a second switch, so the page, the wire format and the enum cannot
    /// drift into three different opinions about what a microgram is called.
    /// </remarks>
    private static string SymbolOf(NutrientUnit unit) =>
        typeof(NutrientUnit)
            .GetField(unit.ToString())!
            .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()!
            .Name;

    private sealed record DocumentedNutrient(
        string DisplayName,
        string Unit,
        int SortOrder,
        NutrientGroup Group);
}
