using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Translates Open Food Facts' nutriment names into Trackr's nutrient keys.
/// </summary>
/// <remarks>
/// A table rather than a naming convention, because the two vocabularies disagree in ways no rule
/// would predict: OFF spells niacin <c>vitamin-pp</c>, spells fibre <c>fiber</c>, and calls protein
/// <c>proteins</c>. <c>NutrientSeed</c> deliberately keeps Trackr's own spellings and leaves the
/// translating to this file.
/// <para>
/// <strong>Units: every value OFF reports under a <c>_100g</c> or <c>_serving</c> suffix is in
/// grams</strong> for anything with a mass, and in kilocalories for <c>energy-kcal</c> - regardless
/// of what the label said. The sibling <c>_unit</c> and <c>_value</c> fields carry the label's own
/// units and are ignored here on purpose. This was verified against live products rather than
/// assumed: a US tin of Pringles declaring iron in milligrams comes back as
/// <c>iron_100g: 0.00107</c>. Reading <c>_value</c> instead would put milligrams in a grams column
/// and be a thousandfold wrong with no symptom.
/// </para>
/// </remarks>
public static class OpenFoodFactsNutrients
{
    /// <summary>
    /// The <c>fields</c> query parameter, so a lookup transfers a few hundred bytes rather than the
    /// entire product record.
    /// </summary>
    /// <remarks>
    /// <c>nutriments_estimated</c> is <strong>not</strong> requested, and must not be read even
    /// though OFF sometimes includes it anyway. Those figures are inferred from the ingredient list,
    /// not read off a label, and storing them as though a source had measured them is precisely the
    /// silent-wrong-number failure CLAUDE.md section 2 names. Estimating is the model's job, and the
    /// model's estimates arrive flagged.
    /// </remarks>
    public const string Fields =
        "code,product_name,product_name_en,brands,serving_size,serving_quantity,"
        + "serving_quantity_unit,nutrition_data_per,nutriments";

    /// <summary>OFF's name for energy in kilocalories, which is already Trackr's stored unit.</summary>
    public const string EnergyStem = "energy-kcal";

    public const string FatStem = "fat";

    public const string ProteinStem = "proteins";

    /// <summary>
    /// Carbohydrate, most-preferred first.
    /// </summary>
    /// <remarks>
    /// Two stems because US labels declare "Total Carbohydrate" and OFF keeps that as a separate
    /// nutriment: a tin of Pringles carries <c>carbohydrates</c> (50 g) <em>and</em>
    /// <c>carbohydrates-total</c> (57.1 g) at once. <c>carbohydrates</c> wins because it is the key
    /// OFF populates for every product worldwide, and falling back keeps US-only entries from
    /// looking like a partial match.
    /// </remarks>
    public static readonly string[] CarbohydrateStems = ["carbohydrates", "carbohydrates-total"];

    /// <summary>
    /// Every non-core nutrient Trackr tracks, and the OFF stems that can supply it, in preference
    /// order.
    /// </summary>
    /// <remarks>
    /// The core four are absent by design - they are columns rather than map entries, so they have
    /// their own stems above. A test asserts this table covers every non-core nutrient in
    /// <see cref="Data.NutrientSeed"/>, so adding "selenium" there fails the build until its OFF
    /// name is recorded here.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string[]> OffStemsByNutrientKey =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["saturated_fat"] = ["saturated-fat"],
            ["trans_fat"] = ["trans-fat"],
            ["monounsaturated_fat"] = ["monounsaturated-fat"],
            ["polyunsaturated_fat"] = ["polyunsaturated-fat"],
            ["cholesterol"] = ["cholesterol"],
            ["sodium"] = ["sodium"],

            // OFF spells it the American way; Trackr does not (see NutrientSeed). Both stems are
            // accepted because some older entries carry the British spelling.
            ["fibre"] = ["fiber", "fibre"],
            ["sugars"] = ["sugars"],
            ["added_sugars"] = ["added-sugars"],

            ["vitamin_d"] = ["vitamin-d"],
            ["vitamin_c"] = ["vitamin-c"],
            ["vitamin_a"] = ["vitamin-a"],
            ["vitamin_e"] = ["vitamin-e"],

            // phylloquinone is vitamin K1, which is what a label means by "vitamin K".
            ["vitamin_k"] = ["vitamin-k", "phylloquinone"],
            ["vitamin_b1"] = ["vitamin-b1"],
            ["vitamin_b2"] = ["vitamin-b2"],

            // vitamin-pp is OFF's canonical name for niacin; vitamin-b3 appears on newer entries.
            ["vitamin_b3"] = ["vitamin-pp", "vitamin-b3"],
            ["vitamin_b6"] = ["vitamin-b6"],
            ["vitamin_b9"] = ["vitamin-b9", "folates"],
            ["vitamin_b12"] = ["vitamin-b12"],

            ["calcium"] = ["calcium"],
            ["iron"] = ["iron"],
            ["potassium"] = ["potassium"],
            ["magnesium"] = ["magnesium"],
            ["zinc"] = ["zinc"]
        };

    /// <summary>
    /// How many of a nutrient's own units make up one gram, so an OFF value can be converted into
    /// the unit its catalog row is recorded in.
    /// </summary>
    /// <remarks>
    /// Returns null for <see cref="NutrientUnit.Kilocalorie"/>, which has no mass to convert. That
    /// is unreachable through the map above - energy is core - and is handled rather than thrown so
    /// a future energy-like nutrient degrades to "not measured" instead of a 500.
    /// </remarks>
    public static decimal? PerGram(NutrientUnit unit) => unit switch
    {
        NutrientUnit.Gram => 1m,
        NutrientUnit.Milligram => 1_000m,
        NutrientUnit.Microgram => 1_000_000m,
        _ => null
    };
}
