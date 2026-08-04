using System.Text.Json;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Turns an Open Food Facts product record into a per-serving <see cref="ProductDraft"/>.
/// </summary>
/// <remarks>
/// Separate from <see cref="OpenFoodFactsClient"/>, and pure, so the interesting half of this
/// milestone is testable against captured JSON with no network in the way. The client's job is
/// transport and failure wording; everything about what the numbers <em>mean</em> is here.
/// <para>
/// <strong>The one invariant to preserve: everything this produces is per one serving</strong>, in
/// each nutrient's own catalog unit. OFF reports per-100 and per-serving figures in grams; the
/// conversion happens once, here, exactly as the remarks on <see cref="FoodItem"/> promise.
/// </para>
/// </remarks>
public static class OpenFoodFactsMapper
{
    /// <summary>What OFF's per-100 figures are per. Never used to build a key name.</summary>
    private const decimal ReferenceQuantity = 100m;

    /// <summary>
    /// A serving bigger than this is a data-entry error rather than a serving - 10 kg of crisps.
    /// Treated as though no serving size were given, which falls back to the per-100 basis.
    /// </summary>
    private const decimal ImplausibleServing = 10_000m;

    /// <summary>Column limits from <see cref="FoodItem"/>, so a draft cannot fail validation later.</summary>
    private const int MaxNameLength = 200;

    private const int MaxBrandLength = 120;

    private const int MaxUnitLength = 32;

    public static ProductLookupResult Map(
        string requestedBarcode,
        OpenFoodFactsProduct product,
        NutrientCatalog catalog)
    {
        var nutriments = product.Nutriments;
        var warnings = new List<string>();

        var basis = ChooseBasis(product, nutriments, out var suffix, out var scale, out var fallback);

        var referenceUnit = ReferenceUnit(product.NutritionDataPer);
        var servingQuantity = ServingQuantity(product);

        var (servingSize, servingUnit) = basis switch
        {
            // Per-serving figures with no serving size to attach them to. "1 serving" is the honest
            // description: the numbers are right, the size is simply unstated.
            ServingBasis.LabelServing when servingQuantity is null => (1m, "serving"),

            ServingBasis.LabelServing or ServingBasis.ScaledFromReferenceQuantity =>
                (servingQuantity!.Value, Unit(product.ServingQuantityUnit) ?? referenceUnit),

            _ => (ReferenceQuantity, referenceUnit)
        };

        if (basis is ServingBasis.ReferenceQuantityAsServing)
        {
            // Section 5's rule: the user is told when a fallback happened, so they can judge the
            // numbers rather than discover the assumption by being surprised by a total.
            warnings.Add(
                $"Open Food Facts had no serving size for this product, so one serving is taken to "
                    + $"be {ReferenceQuantity:0} {referenceUnit}.");
        }

        var energyKcal = Read(OpenFoodFactsNutrients.EnergyStem);
        var fatG = Read(OpenFoodFactsNutrients.FatStem);
        var carbohydrateG = Read(OpenFoodFactsNutrients.CarbohydrateStems);
        var proteinG = Read(OpenFoodFactsNutrients.ProteinStem);

        var nutrients = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var (key, stems) in OpenFoodFactsNutrients.OffStemsByNutrientKey)
        {
            // A key the catalog does not know has no unit, so there is nowhere to put the number.
            // Unreachable while the coverage test passes, and skipped rather than thrown so a
            // half-finished nutrient addition degrades instead of breaking every lookup.
            if (!catalog.TryGet(key, out var definition))
            {
                continue;
            }

            if (OpenFoodFactsNutrients.PerGram(definition.Unit) is not { } perGram)
            {
                continue;
            }

            if (Read(stems) is not { } grams)
            {
                continue;
            }

            // Rounding here can turn a trace amount into 0.0000, which reads as "known to be zero"
            // rather than "trace". At four decimal places that is a tenth of a microgram of a
            // milligram-scale nutrient, which no label distinguishes either.
            nutrients[key] = StoredPrecision.Amount(grams * perGram);
        }

        var name = Truncate(
            OpenFoodFactsValues.Text(product.ProductNameEnglish)
                ?? OpenFoodFactsValues.Text(product.ProductName),
            MaxNameLength);

        var draft = new ProductDraft(
            Barcode: Digits(product.Code) ?? requestedBarcode,
            Name: name,
            Brand: FirstBrand(product.Brands),
            ServingSize: StoredPrecision.Measure(servingSize),
            ServingUnit: Truncate(servingUnit, MaxUnitLength)!,
            ServingBasis: basis,
            EnergyKcal: Round(energyKcal),
            FatG: Round(fatG),
            CarbohydrateG: Round(carbohydrateG),
            ProteinG: Round(proteinG),
            Nutrients: nutrients);

        // What the model would have to determine from the photo. Section 5 calls a match "full" only
        // when calories and macros are there; a nameless product is no use either, since the
        // confirmation card has nothing to show and the catalog nothing to file it under.
        var missing = new List<string>();

        if (name is null)
        {
            missing.Add("name");
        }

        if (energyKcal is null)
        {
            missing.Add("energy");
        }

        if (fatG is null)
        {
            missing.Add("fat");
        }

        if (carbohydrateG is null)
        {
            missing.Add("carbohydrate");
        }

        if (proteinG is null)
        {
            missing.Add("protein");
        }

        return missing.Count == 0
            ? ProductLookupResult.Matched(draft, warnings)
            : ProductLookupResult.Partial(draft, missing, warnings);

        // Reads the first stem that has a value, in the basis chosen for the whole product.
        decimal? Read(params string[] stems)
        {
            foreach (var stem in stems)
            {
                if (OpenFoodFactsValues.Number(nutriments, stem + suffix) is { } value)
                {
                    return value * scale;
                }

                // A nutrient recorded under only the other suffix is still worth having, as long as
                // there is a factor that converts it onto the same basis as the rest. Without this,
                // a product whose macros came per serving would silently drop a micronutrient that
                // OFF happened to store only per 100 g.
                if (fallback is { } fallbackScale
                    && OpenFoodFactsValues.Number(nutriments, stem + OtherSuffix(suffix)) is { } other)
                {
                    return other * fallbackScale;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Decides, once for the whole product, which set of figures to read and how to scale them.
    /// </summary>
    /// <remarks>
    /// One decision for the product rather than one per nutrient, which is the subtle part. Mixing
    /// a per-serving protein with a per-100 g fat would produce a card whose numbers each came from
    /// somewhere defensible and whose totals were nonsense.
    /// </remarks>
    /// <param name="suffix">The nutriment key suffix to read - <c>_serving</c> or <c>_100g</c>.</param>
    /// <param name="scale">What to multiply those values by to get one serving.</param>
    /// <param name="fallback">
    /// The factor for a value found under the <em>other</em> suffix, or null when there is no honest
    /// way to convert one.
    /// </param>
    private static ServingBasis ChooseBasis(
        OpenFoodFactsProduct product,
        IReadOnlyDictionary<string, JsonElement>? nutriments,
        out string suffix,
        out decimal scale,
        out decimal? fallback)
    {
        var servingQuantity = ServingQuantity(product);

        if (HasPerServingFigures(nutriments))
        {
            suffix = "_serving";
            scale = 1m;
            fallback = servingQuantity / ReferenceQuantity;

            return ServingBasis.LabelServing;
        }

        suffix = "_100g";

        if (servingQuantity is { } quantity)
        {
            scale = quantity / ReferenceQuantity;
            fallback = 1m;

            return ServingBasis.ScaledFromReferenceQuantity;
        }

        scale = 1m;

        // No fallback: per-serving values exist for no core nutrient and there is no serving size,
        // so a stray _serving figure could not be placed on the same footing as the rest.
        fallback = null;

        return ServingBasis.ReferenceQuantityAsServing;
    }

    /// <summary>
    /// True when OFF published per-serving figures, judged by the nutrients every product has.
    /// </summary>
    /// <remarks>
    /// Judged on the core stems rather than "any key ending in _serving", because OFF attaches
    /// <c>nova-group_serving</c> to products that have no per-serving nutrition at all - a score,
    /// not a measurement. Testing for that would pick the per-serving basis for a product with no
    /// per-serving nutrients and read nothing but nulls out of it.
    /// </remarks>
    private static bool HasPerServingFigures(IReadOnlyDictionary<string, JsonElement>? nutriments)
    {
        if (nutriments is null)
        {
            return false;
        }

        string[] coreStems =
        [
            OpenFoodFactsNutrients.EnergyStem,
            OpenFoodFactsNutrients.FatStem,
            OpenFoodFactsNutrients.ProteinStem,
            .. OpenFoodFactsNutrients.CarbohydrateStems
        ];

        return coreStems.Any(stem => OpenFoodFactsValues.Number(nutriments, stem + "_serving") is not null);
    }

    private static string OtherSuffix(string suffix) => suffix == "_serving" ? "_100g" : "_serving";

    /// <summary>The serving size as a usable number, or null if absent, zero or absurd.</summary>
    private static decimal? ServingQuantity(OpenFoodFactsProduct product) =>
        OpenFoodFactsValues.Number(product.ServingQuantity) is { } quantity
            && quantity > 0
            && quantity <= ImplausibleServing
            ? quantity
            : null;

    /// <summary>
    /// Grams or millilitres, from <c>nutrition_data_per</c>.
    /// </summary>
    /// <remarks>
    /// Defaults to grams, which is what OFF means when the field is missing. A drink whose entry
    /// forgot to say "100ml" therefore gets "g" - wrong as a label, harmless as arithmetic, since
    /// a millilitre of a soft drink is about a gram and the user can correct the unit on the card.
    /// </remarks>
    private static string ReferenceUnit(string? nutritionDataPer) =>
        nutritionDataPer?.Contains("ml", StringComparison.OrdinalIgnoreCase) is true ? "ml" : "g";

    private static string? Unit(string? value) => Truncate(OpenFoodFactsValues.Text(value), MaxUnitLength);

    /// <summary>Takes the first of OFF's comma-separated brand list.</summary>
    private static string? FirstBrand(string? brands) =>
        Truncate(
            OpenFoodFactsValues.Text(brands)?.Split(',', StringSplitOptions.TrimEntries)
                .FirstOrDefault(part => part.Length > 0),
            MaxBrandLength);

    /// <summary>Keeps only the digits, so OFF's own normalised code can be trusted as a barcode.</summary>
    private static string? Digits(string? code)
    {
        var text = OpenFoodFactsValues.Text(code);

        if (text is null)
        {
            return null;
        }

        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());

        return digits.Length > 0 ? digits : null;
    }

    private static decimal? Round(decimal? value) =>
        value is null ? null : StoredPrecision.Amount(value.Value);

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}
