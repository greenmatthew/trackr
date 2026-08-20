using System.Globalization;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.Nutrition;

/// <summary>
/// Turning a nutrient map into lines a person reads.
/// </summary>
/// <remarks>
/// Shared between the confirmation card and the stats views, which want exactly the same thing:
/// the nutrients something actually reported, in label order, each against its own unit.
/// <para>
/// Formatted here rather than in XAML because the unit belongs to the nutrient rather than to the
/// amount, and a template that formatted its own would need the catalog to reach the view.
/// </para>
/// </remarks>
public static class NutrientRows
{
    /// <summary>
    /// The micronutrients present in <paramref name="amounts"/>, in the catalog's order.
    /// </summary>
    /// <remarks>
    /// <strong>Only what the source reported.</strong> Never a row per known nutrient with a dash
    /// against the ones nobody measured: absent means "not measured" throughout this codebase, and
    /// a list of blanks reads as a list of zeroes.
    /// </remarks>
    public static IReadOnlyList<NutrientRow> Build(
        IReadOnlyDictionary<string, decimal> amounts,
        IReadOnlyDictionary<string, NutrientResponse>? catalog)
    {
        if (catalog is null)
        {
            // No names and no units to render against. Showing raw keys would be worse than showing
            // nothing, and the calories and macros beside these do not need the catalog.
            return [];
        }

        return
        [
            .. amounts
                .Where(amount => catalog.ContainsKey(amount.Key))
                .Select(amount => (Nutrient: catalog[amount.Key], amount.Value))
                .Where(pair => !pair.Nutrient.IsCore)
                .OrderBy(pair => pair.Nutrient.SortOrder)
                .Select(pair => new NutrientRow(
                    pair.Nutrient.DisplayName,
                    $"{Format(pair.Value)} {UnitSymbol(pair.Nutrient.Unit)}"))
        ];
    }

    public static string UnitSymbol(NutrientUnit unit) => unit switch
    {
        NutrientUnit.Gram => "g",
        NutrientUnit.Milligram => "mg",
        NutrientUnit.Microgram => "µg",
        _ => "kcal"
    };

    /// <summary>
    /// Trailing zeros trimmed, so 78.00 reads as 78.
    /// </summary>
    /// <remarks>
    /// The current culture, because these are read by a person.
    /// </remarks>
    public static string Format(decimal value) =>
        value.ToString("0.####", CultureInfo.CurrentCulture);
}

/// <summary>One micronutrient line: "Vitamin C", "12 mg".</summary>
public sealed record NutrientRow(string DisplayName, string Amount);
