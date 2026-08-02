using Trackr.Shared.Nutrition;

namespace Trackr.Api.Data;

/// <summary>One nutrient as the code defines it, before it becomes a row.</summary>
/// <remarks>
/// Definitions rather than <see cref="Nutrient"/> instances, deliberately: a shared static entity
/// instance handed to a scoped <c>DbContext</c> would be tracked by the first one that saw it, and
/// the test factory boots a second host inside the same process. The seeder constructs a fresh
/// entity every time.
/// </remarks>
public sealed record NutrientDefinition(
    string Key,
    string DisplayName,
    NutrientUnit Unit,
    NutrientGroup Group,
    int SortOrder,
    bool IsCore);

/// <summary>
/// The nutrient set Trackr ships with, in nutrition-label order.
/// </summary>
/// <remarks>
/// This list and wiki/Nutrient-Reference.md are held together by a test, not by discipline
/// (CLAUDE.md section 0) - editing one without the other fails the build.
/// <para>
/// Naming, because two of these look like typos and are not: <strong><c>fibre</c>, not
/// <c>fiber</c></strong>, matching CLAUDE.md section 5 and the wiki - Open Food Facts' own
/// <c>fiber</c> and <c>vitamin-pp</c> are OFF's keys, and translating them is milestone 7's job,
/// not this table's. And <c>vitamin_b1</c> through <c>vitamin_b12</c> rather than
/// <c>thiamin</c>/<c>riboflavin</c>, so the B vitamins sort and read together.
/// </para>
/// <para>
/// Sort orders are spaced by 10 so adding selenium later inserts between two numbers instead of
/// renumbering the set. <see cref="NutrientDefinition.Group"/> and
/// <see cref="NutrientDefinition.SortOrder"/> are independent: a label interleaves the core four
/// with their own breakdowns, so the groups are not contiguous here.
/// </para>
/// </remarks>
public static class NutrientSeed
{
    public static readonly IReadOnlyList<NutrientDefinition> All =
    [
        new(CoreNutrients.EnergyKcal, "Energy", NutrientUnit.Kilocalorie, NutrientGroup.Core, 10, true),

        new(CoreNutrients.Fat, "Total fat", NutrientUnit.Gram, NutrientGroup.Core, 20, true),
        new("saturated_fat", "Saturated fat", NutrientUnit.Gram, NutrientGroup.FatBreakdown, 30, false),
        new("trans_fat", "Trans fat", NutrientUnit.Gram, NutrientGroup.FatBreakdown, 40, false),
        new("monounsaturated_fat", "Monounsaturated fat", NutrientUnit.Gram, NutrientGroup.FatBreakdown, 50, false),
        new("polyunsaturated_fat", "Polyunsaturated fat", NutrientUnit.Gram, NutrientGroup.FatBreakdown, 60, false),

        new("cholesterol", "Cholesterol", NutrientUnit.Milligram, NutrientGroup.SterolsAndElectrolytes, 70, false),
        new("sodium", "Sodium", NutrientUnit.Milligram, NutrientGroup.SterolsAndElectrolytes, 80, false),

        new(CoreNutrients.Carbohydrate, "Total carbohydrate", NutrientUnit.Gram, NutrientGroup.Core, 90, true),
        new("fibre", "Dietary fibre", NutrientUnit.Gram, NutrientGroup.CarbohydrateBreakdown, 100, false),
        new("sugars", "Total sugars", NutrientUnit.Gram, NutrientGroup.CarbohydrateBreakdown, 110, false),
        new("added_sugars", "Added sugars", NutrientUnit.Gram, NutrientGroup.CarbohydrateBreakdown, 120, false),

        new(CoreNutrients.Protein, "Protein", NutrientUnit.Gram, NutrientGroup.Core, 130, true),

        new("vitamin_d", "Vitamin D", NutrientUnit.Microgram, NutrientGroup.Vitamins, 140, false),
        new("vitamin_c", "Vitamin C", NutrientUnit.Milligram, NutrientGroup.Vitamins, 150, false),
        new("vitamin_a", "Vitamin A", NutrientUnit.Microgram, NutrientGroup.Vitamins, 160, false),
        new("vitamin_e", "Vitamin E", NutrientUnit.Milligram, NutrientGroup.Vitamins, 170, false),
        new("vitamin_k", "Vitamin K", NutrientUnit.Microgram, NutrientGroup.Vitamins, 180, false),
        new("vitamin_b1", "B1 (thiamin)", NutrientUnit.Milligram, NutrientGroup.Vitamins, 190, false),
        new("vitamin_b2", "B2 (riboflavin)", NutrientUnit.Milligram, NutrientGroup.Vitamins, 200, false),
        new("vitamin_b3", "B3 (niacin)", NutrientUnit.Milligram, NutrientGroup.Vitamins, 210, false),
        new("vitamin_b6", "B6", NutrientUnit.Milligram, NutrientGroup.Vitamins, 220, false),
        new("vitamin_b9", "B9 (folate)", NutrientUnit.Microgram, NutrientGroup.Vitamins, 230, false),
        new("vitamin_b12", "B12", NutrientUnit.Microgram, NutrientGroup.Vitamins, 240, false),

        new("calcium", "Calcium", NutrientUnit.Milligram, NutrientGroup.Minerals, 250, false),
        new("iron", "Iron", NutrientUnit.Milligram, NutrientGroup.Minerals, 260, false),
        new("potassium", "Potassium", NutrientUnit.Milligram, NutrientGroup.Minerals, 270, false),
        new("magnesium", "Magnesium", NutrientUnit.Milligram, NutrientGroup.Minerals, 280, false),
        new("zinc", "Zinc", NutrientUnit.Milligram, NutrientGroup.Minerals, 290, false),
    ];
}
