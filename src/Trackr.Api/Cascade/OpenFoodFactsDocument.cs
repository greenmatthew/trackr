using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Trackr.Api.Cascade;

/// <summary>
/// The Open Food Facts v2 product response, as far as Trackr reads it.
/// </summary>
/// <remarks>
/// <see cref="Status"/> is an integer (1 found, 0 not found) rather than the string a reader might
/// expect from a "v2" API. Verified against the live service, not inferred.
/// </remarks>
public sealed record OpenFoodFactsResponse
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("status_verbose")]
    public string? StatusVerbose { get; init; }

    [JsonPropertyName("product")]
    public OpenFoodFactsProduct? Product { get; init; }
}

/// <summary>
/// One product record.
/// </summary>
/// <remarks>
/// <strong>Loosely typed on purpose where OFF is loosely typed.</strong> This is a crowd-edited
/// database with two decades of history behind it: numbers arrive as JSON numbers on one product
/// and as quoted strings on the next, and a <c>decimal</c> property here would throw on the second
/// one and lose the whole record over one field. The <see cref="JsonElement"/> fields go through
/// <see cref="OpenFoodFactsValues.Number"/>, which accepts either.
/// </remarks>
public sealed record OpenFoodFactsProduct
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("product_name")]
    public string? ProductName { get; init; }

    /// <summary>
    /// The English name, when the entry has one. Preferred over <see cref="ProductName"/>, which is
    /// whatever language the contributor used.
    /// </summary>
    [JsonPropertyName("product_name_en")]
    public string? ProductNameEnglish { get; init; }

    /// <summary>
    /// A comma-separated list, not one brand: Nutella comes back as
    /// "Nutella, Ferrero, Yum yum". The first entry is the one to keep.
    /// </summary>
    [JsonPropertyName("brands")]
    public string? Brands { get; init; }

    /// <summary>Free text as printed, e.g. "1 serving (28 g)". Not machine-readable.</summary>
    [JsonPropertyName("serving_size")]
    public string? ServingSize { get; init; }

    /// <summary>The serving size as a number, in <see cref="ServingQuantityUnit"/>.</summary>
    [JsonPropertyName("serving_quantity")]
    public JsonElement ServingQuantity { get; init; }

    [JsonPropertyName("serving_quantity_unit")]
    public string? ServingQuantityUnit { get; init; }

    /// <summary>
    /// What the per-100 figures are per: <c>100g</c>, <c>100ml</c> or <c>serving</c>.
    /// </summary>
    /// <remarks>
    /// Read for the <em>unit</em> only, never for the suffix. OFF names the keys <c>_100g</c> even
    /// for a drink measured per 100 ml - Diet Coke reports <c>nutrition_data_per: "100ml"</c>
    /// alongside <c>carbohydrates_100g</c>. Deriving the key name from this field would find nothing
    /// for every liquid in the database.
    /// </remarks>
    [JsonPropertyName("nutrition_data_per")]
    public string? NutritionDataPer { get; init; }

    /// <summary>
    /// The flat nutriment bag: <c>{stem}_100g</c>, <c>{stem}_serving</c>, plus <c>_unit</c>,
    /// <c>_value</c> and <c>_modifier</c> siblings that Trackr ignores.
    /// </summary>
    [JsonPropertyName("nutriments")]
    public Dictionary<string, JsonElement>? Nutriments { get; init; }

    /// <summary>The ingredient list in English, when the entry has one.</summary>
    [JsonPropertyName("ingredients_text_en")]
    public string? IngredientsTextEnglish { get; init; }

    /// <summary>
    /// The ingredient list in whatever language the contributor used.
    /// </summary>
    /// <remarks>
    /// Kept as a fallback rather than skipped. A French ingredient list is a worse answer to "what
    /// is in this" than an English one and a much better answer than nothing, and the alternative -
    /// asking the model to read a label the database already has - costs a photograph's worth of
    /// tokens to get the same words in a different order.
    /// </remarks>
    [JsonPropertyName("ingredients_text")]
    public string? IngredientsText { get; init; }

    /// <summary>Declared allergens as taxonomy tags: <c>en:milk</c>, <c>en:nuts</c>.</summary>
    [JsonPropertyName("allergens_tags")]
    public List<string>? AllergensTags { get; init; }

    /// <summary>
    /// What OFF's own ingredient analysis concluded: <c>en:palm-oil</c>, <c>en:non-vegan</c>.
    /// </summary>
    /// <remarks>
    /// Inferences drawn from the ingredient list rather than claims a label made, and OFF says so
    /// itself with tags like <c>en:vegan-status-unknown</c>. Carried as reported, including the
    /// unknowns - dropping those would turn "nobody could tell" into silence, which reads as a
    /// stronger statement than it is.
    /// </remarks>
    [JsonPropertyName("ingredients_analysis_tags")]
    public List<string>? IngredientsAnalysisTags { get; init; }
}

/// <summary>
/// Reads numbers out of Open Food Facts JSON without trusting its types.
/// </summary>
public static class OpenFoodFactsValues
{
    /// <summary>
    /// A non-negative number, or null if the field is absent, empty, unparseable, out of
    /// <see cref="decimal"/> range or negative.
    /// </summary>
    /// <remarks>
    /// Null for anything doubtful, deliberately, because null means "not measured" downstream and a
    /// missing micronutrient is a fine outcome (CLAUDE.md section 7) while a wrong one is not.
    /// <para>
    /// Negatives are refused rather than clamped to zero: a negative nutrient amount is a data-entry
    /// error, and zero would assert "known to be zero", which is a different claim from "we do not
    /// know". Doubles beyond decimal range come back null the same way, which also covers the
    /// occasional entry where somebody typed a phone number into a nutrient field.
    /// </para>
    /// </remarks>
    public static decimal? Number(JsonElement element)
    {
        var value = element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out var number) ? number : null,
            JsonValueKind.String => decimal.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null,
            _ => (decimal?)null
        };

        return value is >= 0 ? value : null;
    }

    /// <summary>Looks up a nutriment and reads it, in one step.</summary>
    public static decimal? Number(IReadOnlyDictionary<string, JsonElement>? nutriments, string key) =>
        nutriments is not null && nutriments.TryGetValue(key, out var element) ? Number(element) : null;

    /// <summary>Trims, and turns whitespace-only into null - OFF uses "" where it means absent.</summary>
    public static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
