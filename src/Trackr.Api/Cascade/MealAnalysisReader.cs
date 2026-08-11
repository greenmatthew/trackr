using System.Globalization;
using System.Text.Json;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Turns whatever the model said into something the server is willing to show somebody.
/// </summary>
/// <remarks>
/// <strong>This class is the milestone.</strong> CLAUDE.md section 5 marks it REQUIRED and says why:
/// small local models emit broken JSON and numbers that do not add up, and this is the main thing
/// standing between the model and a wrong number in the database. Everything else in the cascade is
/// plumbing by comparison.
/// <para>
/// The rule it is written against is docs/decisions/08-barcode-off.md's closing lesson - <em>a
/// confident wrong answer is the expensive failure</em> - so the four things it can do to a value
/// are graded by how confident the result would look:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <strong>Drop</strong> - the value becomes "not measured". Never clamped to zero, because zero is
/// a claim somebody made and this is the absence of one.
/// </description></item>
/// <item><description><strong>Warn</strong> - kept, with a sentence saying what was done to it.</description></item>
/// <item><description>
/// <strong>Flag</strong> - the item is marked low-confidence and still returned. Section 5's
/// explicit remedy for figures that do not reconcile: show it, do not present it as fact.
/// </description></item>
/// <item><description>
/// <strong>Fail</strong> - nothing is returned at all. Reserved for answers whose shape means the
/// grammar was not applied, which is a fault in this server rather than a bad reading.
/// </description></item>
/// </list>
/// <para>
/// Pure, so every one of those paths is a unit test with no container and no model behind it.
/// </para>
/// </remarks>
public static class MealAnalysisReader
{
    /// <summary>The ceiling of the <c>numeric(12,4)</c> columns a confirmed item lands in.</summary>
    /// <remarks>
    /// Load-bearing rather than theoretical. <c>LogEndpoints</c> multiplies per-serving values by
    /// the quantity <em>before</em> storing, so a large enough pair overflows the column - and it
    /// does so in milestone 9, on a card the user has already approved, as a 500 from Postgres. The
    /// grammar cannot prevent it: an unconstrained JSON number may have sixteen digits either side
    /// of the point.
    /// </remarks>
    private const decimal MaxStoredAmount = 99_999_999.9999m;

    /// <summary>Nobody eats a thousand servings of anything. Above this is a misread, not a meal.</summary>
    private const decimal MaxQuantity = 1_000m;

    /// <summary>Ten kilograms is not a serving. Matches the mapper's own limit.</summary>
    private const decimal MaxServingSize = 10_000m;

    /// <summary>A kilogram of butter is about 7 200 kcal, so a serving past this is worth a look.</summary>
    private const decimal ImplausibleEnergyKcal = 5_000m;

    /// <summary>No single nutrient weighs a kilogram, whatever the serving turns out to be.</summary>
    private const decimal MaxNutrientGrams = 1_000m;

    /// <summary>Atwater factors - kilocalories per gram of each macronutrient.</summary>
    private const decimal ProteinKcalPerGram = 4m;

    private const decimal CarbohydrateKcalPerGram = 4m;

    private const decimal FatKcalPerGram = 9m;

    /// <summary>
    /// How far energy may sit from the macros before the item is flagged.
    /// </summary>
    /// <remarks>
    /// A quarter, not a tenth, and the reason is arithmetic rather than generosity: Atwater ignores
    /// fibre (about 2 kcal/g), sugar alcohols (about 2.4) and alcohol (7), none of which Trackr
    /// tracks. A high-fibre cereal or a beer misses by 10-40% while every number on it is correct,
    /// and a check that fires on those is a check people learn to ignore.
    /// </remarks>
    private const decimal EnergyTolerance = 0.25m;

    /// <summary>
    /// The floor under that band, in kilocalories.
    /// </summary>
    /// <remarks>
    /// At 40 kcal a quarter is 10 kcal, and rounding each macro to the nearest gram - which is what
    /// a label does - can move the prediction by 9 on its own.
    /// </remarks>
    private const decimal EnergyToleranceFloor = 50m;

    /// <summary>
    /// Slack on a breakdown-against-total comparison: 5% and half a gram.
    /// </summary>
    /// <remarks>
    /// The half gram is not padding. US labels round fat lines to the nearest 0.5 g below 5 g, so a
    /// correctly transcribed label can genuinely have its components sum past its own total.
    /// </remarks>
    private const decimal BreakdownTolerance = 1.05m;

    private const decimal BreakdownSlackGrams = 0.5m;

    private const int MaxNoteLength = 300;

    private const int MaxNameLength = 200;

    private const int MaxBrandLength = 120;

    private const int MaxUnitLength = 32;

    /// <summary>Ollama's <c>done_reason</c> when generation hit the output cap.</summary>
    private const string TruncatedReason = "length";

    /// <summary>
    /// Reads one reply.
    /// </summary>
    /// <param name="content">The model's message content, which should be a single JSON object.</param>
    /// <param name="doneReason">Ollama's <c>done_reason</c>, or null if it did not say.</param>
    /// <param name="productReferences">
    /// The handles this request actually offered. Anything else the model quotes back is a
    /// hallucinated reference and is treated as though it had said none.
    /// </param>
    public static ModelReading Read(
        string? content,
        string? doneReason,
        NutrientCatalog catalog,
        IReadOnlySet<string> productReferences)
    {
        // Checked before parsing, because truncated JSON fails to parse for a reason worth naming.
        // "The model ran out of room" and "the model ignored the schema" have different fixes - one
        // is a setting on this server, the other is a model that cannot do the job.
        if (string.Equals(doneReason, TruncatedReason, StringComparison.OrdinalIgnoreCase))
        {
            return ModelReading.Failed(
                "The local model ran out of room before it finished answering, so its reply was cut "
                    + "off. Try again with fewer photos, or raise the model's output limit.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return ModelReading.Failed("The local model returned an empty reply.");
        }

        JsonDocument document;

        try
        {
            // Parsed strictly. Hunting for the first '{' in a reply wrapped in prose is the obvious
            // robustness improvement and is exactly the class of change that turned three barcode
            // photographs into checksum-valid false positives in milestone 7: it converts a visible
            // failure into an invisible one. If the grammar is working this never happens, and if it
            // is not working that is worth finding out.
            document = JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return ModelReading.Failed(
                "The local model's reply was not valid JSON, so nothing could be read from it. This "
                    + "usually means the model is not following the requested format.");
        }

        using (document)
        {
            return Read(document.RootElement, catalog, productReferences);
        }
    }

    private static ModelReading Read(
        JsonElement root,
        NutrientCatalog catalog,
        IReadOnlySet<string> productReferences)
    {
        if (root.ValueKind is not JsonValueKind.Object)
        {
            return ModelReading.Failed(
                "The local model's reply was not in the shape this server asked for.");
        }

        var note = Truncate(Text(root, "note"), MaxNoteLength);

        if (!root.TryGetProperty("items", out var items) || items.ValueKind is not JsonValueKind.Array)
        {
            return ModelReading.Failed(
                "The local model's reply did not contain a list of foods.",
                note);
        }

        if (items.GetArrayLength() == 0)
        {
            // The honest path, and the reason "note" is a required field. The model saying "the
            // photo is too blurry to read the label" is far more use than this server saying it got
            // nothing back, and section 5 forbids inventing an entry to fill the gap.
            return ModelReading.Failed(
                "The local model could not identify any food in what you sent.",
                note);
        }

        var warnings = new List<string>();
        var read = new List<ModelItem>();

        foreach (var element in items.EnumerateArray())
        {
            if (element.ValueKind is not JsonValueKind.Object)
            {
                warnings.Add("The local model returned something that was not a food, and it was ignored.");
                continue;
            }

            // A core field absent despite being required means the grammar was not applied - the
            // reply is unconstrained and nothing in it can be trusted, including the parts that
            // happen to look right. That is a fault in this server's configuration rather than a bad
            // reading, so the whole analysis fails rather than one item being dropped.
            if (!HasEveryCoreField(element))
            {
                return ModelReading.Failed(
                    "The local model's reply was missing values it was required to provide. The "
                        + "server could not trust any of it.",
                    note);
            }

            if (ReadItem(element, catalog, productReferences, out var item, out var itemWarnings))
            {
                // Its warnings travel on the item, where the card can show them beside the numbers
                // they are about. Only a dropped item has to report upwards, because there is no
                // longer anything for its warning to be attached to.
                read.Add(item);
                continue;
            }

            warnings.AddRange(itemWarnings);
        }

        if (read.Count == 0)
        {
            return ModelReading.Failed(
                "Nothing the local model returned could be used.",
                note);
        }

        return ModelReading.Read(read, note, warnings);
    }

    private static bool HasEveryCoreField(JsonElement element) =>
        Number(element, "energyKcal") is not null
        && Number(element, "fatG") is not null
        && Number(element, "carbohydrateG") is not null
        && Number(element, "proteinG") is not null;

    private static bool ReadItem(
        JsonElement element,
        NutrientCatalog catalog,
        IReadOnlySet<string> productReferences,
        out ModelItem item,
        out List<string> warnings)
    {
        item = null!;
        warnings = [];

        var name = Truncate(Text(element, "name"), MaxNameLength);

        if (name is null)
        {
            warnings.Add("The local model returned a food with no name, and it was ignored.");
            return false;
        }

        var energyKcal = Number(element, "energyKcal") ?? 0m;
        var fatG = Number(element, "fatG") ?? 0m;
        var carbohydrateG = Number(element, "carbohydrateG") ?? 0m;
        var proteinG = Number(element, "proteinG") ?? 0m;

        // The core four are non-nullable columns, so a negative one cannot be stored and cannot be
        // dropped either. Clamping to zero would turn "the model produced nonsense" into "this food
        // contains no protein", which is a claim, and a plausible-looking one.
        if (energyKcal < 0 || fatG < 0 || carbohydrateG < 0 || proteinG < 0)
        {
            warnings.Add($"The local model reported a negative amount for {name}, so it was ignored.");
            return false;
        }

        var confidence = AnalysisConfidence.Normal;

        var quantity = ReadQuantity(element, name, warnings, ref confidence);
        var (servingSize, servingUnit) = ReadServing(element, name, warnings);

        var nutrients = ReadNutrients(element, catalog, name, servingSize, servingUnit, warnings);

        CheckEnergy(name, energyKcal, fatG, carbohydrateG, proteinG, warnings, ref confidence);
        CheckMass(name, fatG, carbohydrateG, proteinG, nutrients, servingSize, servingUnit, warnings, ref confidence);
        CheckBreakdowns(name, fatG, carbohydrateG, nutrients, warnings, ref confidence);

        // Rounded once, at the end, per docs/decisions/09-composites.md. Rounding each intermediate
        // would drift, and rounding here means the numbers the confirmation card shows are exactly
        // the numbers the log stores.
        energyKcal = StoredPrecision.Amount(energyKcal);
        fatG = StoredPrecision.Amount(fatG);
        carbohydrateG = StoredPrecision.Amount(carbohydrateG);
        proteinG = StoredPrecision.Amount(proteinG);

        quantity = CapForStorage(
            name, quantity, [energyKcal, fatG, carbohydrateG, proteinG, .. nutrients.Values], warnings, ref confidence);

        item = new ModelItem(
            ProductReference: ReadProductReference(element, productReferences, name, warnings),
            Name: name,
            Brand: Truncate(Text(element, "brand"), MaxBrandLength),
            Quantity: quantity,
            ServingSize: servingSize,
            ServingUnit: servingUnit,
            EnergyKcal: energyKcal,
            FatG: fatG,
            CarbohydrateG: carbohydrateG,
            ProteinG: proteinG,
            Nutrients: nutrients,
            Confidence: confidence,
            Warnings: warnings);

        return true;
    }

    private static string? ReadProductReference(
        JsonElement element,
        IReadOnlySet<string> productReferences,
        string name,
        List<string> warnings)
    {
        var reference = Text(element, "productRef");

        if (reference is null || string.Equals(reference, MealPrompt.NoProduct, StringComparison.Ordinal))
        {
            return null;
        }

        if (productReferences.Contains(reference))
        {
            return reference;
        }

        // Unreachable while the grammar's enum is in force, which is precisely why it is worth
        // handling: reaching it means constrained decoding is not working.
        warnings.Add(
            $"The local model tied {name} to a product this server never mentioned, so the link was "
                + "ignored.");

        return null;
    }

    /// <summary>
    /// How many servings, with the one deliberate substitution in this class.
    /// </summary>
    /// <remarks>
    /// A missing or absurd quantity becomes 1 rather than dropping the item. Somebody ate something,
    /// the nutrition may be perfectly good, and the quantity is the single easiest field to correct
    /// on the confirmation card - "1" with a warning beside it is far more useful than nothing.
    /// </remarks>
    private static decimal ReadQuantity(
        JsonElement element,
        string name,
        List<string> warnings,
        ref AnalysisConfidence confidence)
    {
        var quantity = Number(element, "quantity");

        if (quantity is null or <= 0)
        {
            warnings.Add(
                $"The local model did not give a usable amount for {name}, so one serving was "
                    + "assumed. Please check it.");

            return 1m;
        }

        if (quantity > MaxQuantity)
        {
            confidence = AnalysisConfidence.Low;
            warnings.Add(
                $"The local model reported an implausible number of servings of {name}, so it was "
                    + "reduced. Please check it.");

            return MaxQuantity;
        }

        return StoredPrecision.Measure(quantity.Value);
    }

    private static (decimal? Size, string? Unit) ReadServing(
        JsonElement element,
        string name,
        List<string> warnings)
    {
        var size = Number(element, "servingSize");
        var unit = Truncate(Text(element, "servingUnit"), MaxUnitLength);

        if (size is null)
        {
            // Normal, not a problem. A plate of food has no serving size, which is why the matching
            // field on SaveLogItemRequest is nullable.
            return (null, unit);
        }

        if (size <= 0 || size > MaxServingSize)
        {
            warnings.Add($"The local model gave an impossible serving size for {name}, so it was dropped.");

            return (null, null);
        }

        return (StoredPrecision.Measure(size.Value), unit);
    }

    private static Dictionary<string, decimal> ReadNutrients(
        JsonElement element,
        NutrientCatalog catalog,
        string name,
        decimal? servingSize,
        string? servingUnit,
        List<string> warnings)
    {
        var nutrients = new Dictionary<string, decimal>(StringComparer.Ordinal);

        if (!element.TryGetProperty("nutrients", out var array) || array.ValueKind is not JsonValueKind.Array)
        {
            return nutrients;
        }

        var seen = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var contradicted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind is not JsonValueKind.Object)
            {
                continue;
            }

            var key = Text(entry, "key");

            if (key is null)
            {
                continue;
            }

            if (CoreNutrients.IsCore(key) || !catalog.TryGet(key, out var definition))
            {
                // Impossible under the schema's enum, so reaching it means the grammar failed open
                // and the reply is unconstrained. Dropped rather than stored: a key with no catalog
                // row has no unit, so there is nowhere to put the number even if it were trusted.
                warnings.Add(
                    $"The local model reported a nutrient this server does not track for {name}, and "
                        + "it was ignored.");
                continue;
            }

            var amount = Number(entry, "amount");

            // Negative, unreadable, or too large for a decimal: all "not measured". Never zero -
            // wiki/Nutrient-Reference.md's rule is that the two are different facts, and this is one
            // of the places it would be easiest to lose.
            if (amount is null or < 0)
            {
                warnings.Add($"The local model gave an unusable {definition.DisplayName} for {name}, so it was dropped.");
                continue;
            }

            var grams = Grams(amount.Value, definition.Unit);

            if (grams is { } mass && ExceedsPlausibleMass(mass, servingSize, servingUnit))
            {
                // A thousandfold unit slip - milligrams written where grams were asked for - shows
                // up here and essentially nowhere else, because every other figure on the item stays
                // internally consistent.
                warnings.Add(
                    $"The local model's {definition.DisplayName} for {name} weighed more than the "
                        + "food does, so it was dropped.");
                continue;
            }

            var rounded = StoredPrecision.Amount(amount.Value);

            if (seen.TryGetValue(key, out var previous))
            {
                if (previous != rounded)
                {
                    // Two different answers about the same nutrient is the model telling you it does
                    // not know. Picking one would be picking at random. The grammar cannot prevent
                    // this - uniqueItems is not a construct it supports.
                    contradicted.Add(key);
                    warnings.Add(
                        $"The local model gave two different values for {definition.DisplayName} in "
                            + $"{name}, so neither was used.");
                }

                continue;
            }

            seen[key] = rounded;
        }

        foreach (var (key, amount) in seen)
        {
            if (!contradicted.Contains(key))
            {
                nutrients[key] = amount;
            }
        }

        return nutrients;
    }

    /// <summary>
    /// Reconciles energy against the macros - the check CLAUDE.md section 5 asks for by name.
    /// </summary>
    /// <remarks>
    /// Flags, never drops and never fails. The energy figure is usually the one read most directly
    /// off a label, so a disagreement says "one of these five numbers is wrong" without saying
    /// which, and the person looking at the card is better placed to tell than this method is.
    /// </remarks>
    private static void CheckEnergy(
        string name,
        decimal energyKcal,
        decimal fatG,
        decimal carbohydrateG,
        decimal proteinG,
        List<string> warnings,
        ref AnalysisConfidence confidence)
    {
        var predicted = (proteinG * ProteinKcalPerGram)
            + (carbohydrateG * CarbohydrateKcalPerGram)
            + (fatG * FatKcalPerGram);

        if (energyKcal > ImplausibleEnergyKcal)
        {
            confidence = AnalysisConfidence.Low;
            warnings.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"One serving of {name} is given as {energyKcal:0} kcal, which is more than a kilogram of butter. Please check it."));
        }

        var allowed = Math.Max(EnergyToleranceFloor, EnergyTolerance * Math.Max(energyKcal, predicted));

        if (Math.Abs(energyKcal - predicted) <= allowed)
        {
            return;
        }

        confidence = AnalysisConfidence.Low;

        warnings.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The calories for {name} ({energyKcal:0.#} kcal) do not match its fat, carbohydrate "
                    + $"and protein (about {predicted:0.#} kcal). Please check them."));
    }

    /// <summary>
    /// Checks that the food weighs at least as much as the things in it.
    /// </summary>
    /// <remarks>
    /// The most valuable check here, and the least obvious. It catches a model reading the
    /// <em>per 100 g</em> column of a dual-column label while reporting the label's 28 g serving -
    /// a reading that is wrong by more than threefold and in which every other consistency check
    /// passes, because the numbers are all correct, just not for the serving they are attached to.
    /// <para>
    /// Only the four that are not subsets of each other are counted. Saturated fat is part of the
    /// fat and sugars are part of the carbohydrate, so adding them would double-count and fire on
    /// perfectly good labels.
    /// </para>
    /// </remarks>
    private static void CheckMass(
        string name,
        decimal fatG,
        decimal carbohydrateG,
        decimal proteinG,
        IReadOnlyDictionary<string, decimal> nutrients,
        decimal? servingSize,
        string? servingUnit,
        List<string> warnings,
        ref AnalysisConfidence confidence)
    {
        if (servingSize is not { } size || !IsMassUnit(servingUnit))
        {
            return;
        }

        var fibre = nutrients.TryGetValue("fibre", out var value) ? value : 0m;
        var total = fatG + carbohydrateG + proteinG + fibre;

        if (total <= size * BreakdownTolerance)
        {
            return;
        }

        confidence = AnalysisConfidence.Low;

        warnings.Add(
            string.Create(
                CultureInfo.InvariantCulture,
                $"The nutrition given for {name} weighs about {total:0.#} g, which is more than its "
                    + $"stated serving of {size:0.#} {servingUnit}. The figures may be per 100 "
                    + $"{servingUnit} rather than per serving."));
    }

    /// <summary>
    /// Checks each breakdown against the total it belongs to.
    /// </summary>
    /// <remarks>
    /// An individual component larger than its total is dropped, because the total is the more
    /// trustworthy of the two - it is required, it is one of the core four, and it has already been
    /// reconciled against the energy. A <em>sum</em> that overshoots only flags, because it does not
    /// say which component is wrong and guessing is what this project does not do.
    /// <para>
    /// <strong>Fibre is deliberately not checked against carbohydrate.</strong> European labels
    /// exclude fibre from the carbohydrate figure and US labels include it, so the obvious rule
    /// fires on nearly every European product. docs/decisions/08-barcode-off.md hit the same
    /// divergence from the other direction; there is a test here whose only job is to stop somebody
    /// adding the check back.
    /// </para>
    /// </remarks>
    private static void CheckBreakdowns(
        string name,
        decimal fatG,
        decimal carbohydrateG,
        Dictionary<string, decimal> nutrients,
        List<string> warnings,
        ref AnalysisConfidence confidence)
    {
        string[] fatComponents = ["saturated_fat", "trans_fat", "monounsaturated_fat", "polyunsaturated_fat"];

        var fatCeiling = (fatG * BreakdownTolerance) + BreakdownSlackGrams;

        foreach (var key in fatComponents)
        {
            if (nutrients.TryGetValue(key, out var component) && component > fatCeiling)
            {
                nutrients.Remove(key);
                warnings.Add(
                    $"The local model reported more {key.Replace('_', ' ')} than total fat for {name}, "
                        + "so that figure was dropped.");
            }
        }

        var fatSum = fatComponents.Sum(key => nutrients.TryGetValue(key, out var value) ? value : 0m);

        if (fatSum > fatCeiling)
        {
            confidence = AnalysisConfidence.Low;
            warnings.Add($"The fat breakdown for {name} adds up to more than its total fat. Please check it.");
        }

        if (nutrients.TryGetValue("sugars", out var sugars))
        {
            if (sugars > (carbohydrateG * BreakdownTolerance) + BreakdownSlackGrams)
            {
                confidence = AnalysisConfidence.Low;
                warnings.Add($"The sugars for {name} exceed its total carbohydrate. Please check them.");
            }

            if (nutrients.TryGetValue("added_sugars", out var added)
                && added > (sugars * BreakdownTolerance) + BreakdownSlackGrams)
            {
                confidence = AnalysisConfidence.Low;
                warnings.Add($"The added sugars for {name} exceed its total sugars. Please check them.");
            }
        }
    }

    /// <summary>
    /// Keeps the quantity low enough that the totals milestone 9 stores will fit their column.
    /// </summary>
    /// <remarks>
    /// The last thing to run, because it depends on the rounded values. Reducing the quantity rather
    /// than the nutrition is the right way round: the nutrition may well have been read correctly
    /// off a label, and it is the count that is implausible.
    /// </remarks>
    private static decimal CapForStorage(
        string name,
        decimal quantity,
        IReadOnlyList<decimal> perServing,
        List<string> warnings,
        ref AnalysisConfidence confidence)
    {
        var largest = perServing.Count == 0 ? 0m : perServing.Max();

        if (largest <= 0 || largest * quantity <= MaxStoredAmount)
        {
            return quantity;
        }

        confidence = AnalysisConfidence.Low;

        warnings.Add(
            $"The amount of {name} the local model reported is larger than this server can record, "
                + "so it was reduced. Please check it.");

        return StoredPrecision.Measure(MaxStoredAmount / largest);
    }

    private static bool ExceedsPlausibleMass(decimal grams, decimal? servingSize, string? servingUnit)
    {
        if (grams > MaxNutrientGrams)
        {
            return true;
        }

        return servingSize is { } size && IsMassUnit(servingUnit) && grams > size * BreakdownTolerance;
    }

    /// <summary>
    /// The nutrient's amount converted to grams, or null when it has no mass to convert.
    /// </summary>
    /// <remarks>
    /// Energy is the only unit without one, and it is core, so this returning null is unreachable
    /// through the nutrient map. Handled rather than thrown so a future energy-like nutrient
    /// degrades into "not checked" instead of a 500.
    /// </remarks>
    private static decimal? Grams(decimal amount, NutrientUnit unit) =>
        OpenFoodFactsNutrients.PerGram(unit) is { } perGram ? amount / perGram : null;

    /// <summary>Grams and millilitres are close enough for food that the check holds for both.</summary>
    private static bool IsMassUnit(string? unit) =>
        unit is not null
        && (unit.Equals("g", StringComparison.OrdinalIgnoreCase)
            || unit.Equals("ml", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A finite number, or null.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="JsonElement"/> rather than by binding to a <c>decimal</c> property,
    /// for the reason <see cref="OpenFoodFactsValues.Number"/> does the same: the grammar permits
    /// <c>1e999999</c>, which throws on deserialisation and would lose a whole meal to an exception
    /// that reads like a transport failure. Strings are accepted too, which the grammar should make
    /// impossible and which costs nothing to allow.
    /// </remarks>
    private static decimal? Number(JsonElement parent, string name)
    {
        if (parent.ValueKind is not JsonValueKind.Object || !parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out var number) ? number : null,
            JsonValueKind.String => decimal.TryParse(
                element.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : null,
            _ => null
        };
    }

    private static string? Text(JsonElement parent, string name)
    {
        if (parent.ValueKind is not JsonValueKind.Object
            || !parent.TryGetProperty(name, out var element)
            || element.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        var value = element.GetString();

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}
