using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Collects field errors so a handler can report all of them at once.
/// </summary>
/// <remarks>
/// Hand-rolled, like the validation everywhere else in this project - the endpoints return
/// <see cref="Results.ValidationProblem(IDictionary{string, string[]}, string, string, int?, string, IDictionary{string, object})"/>
/// directly rather than pulling in a validation framework. This exists only because a nutrient map
/// can produce a dozen complaints in one request, and reporting them one at a time would make
/// fixing a payload a guessing game.
/// </remarks>
internal sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool Any => _errors.Count > 0;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    public IResult Problem() =>
        Results.ValidationProblem(
            _errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.Ordinal));
}

/// <summary>
/// The checks a nutrient map has to pass whichever end of the API it arrived at.
/// </summary>
internal static class NutritionValidation
{
    /// <summary>
    /// Rejects unknown keys, the four core nutrients, and negative amounts.
    /// </summary>
    /// <remarks>
    /// The core-nutrient rule is enforced twice: here, so the caller is told which key was wrong
    /// and why, and by a database CHECK constraint, so it cannot be got around. This is the
    /// friendly half; that one is the reliable half.
    /// <para>
    /// An unknown key is refused rather than ignored. Silently dropping a nutrient somebody sent
    /// would be exactly the "silent wrong number" CLAUDE.md section 2 names as the main failure
    /// mode to avoid - and the key has nowhere to go anyway, since a key with no catalog row has
    /// no unit.
    /// </para>
    /// </remarks>
    public static void ValidateNutrients(
        IReadOnlyDictionary<string, decimal> nutrients,
        NutrientCatalog catalog,
        string field,
        ValidationErrors errors)
    {
        foreach (var (key, amount) in nutrients)
        {
            if (CoreNutrients.IsCore(key))
            {
                errors.Add(
                    field,
                    $"'{key}' is one of the four always-present nutrients, so it belongs in its own "
                        + "field rather than in the nutrient map.");
                continue;
            }

            if (!catalog.Contains(key))
            {
                errors.Add(
                    field,
                    $"'{key}' is not a nutrient this server knows about. GET /api/nutrients lists "
                        + "every key it accepts.");
                continue;
            }

            if (amount < 0)
            {
                errors.Add(field, $"'{key}' cannot be negative.");
            }
        }
    }

    /// <summary>Checks the four values that every item must carry.</summary>
    /// <remarks>
    /// Non-negative rather than non-null: these are columns rather than map entries precisely
    /// because they are always present, which is the one deliberate exception to "missing is not
    /// zero". A source that could not determine the protein is a question for the user during
    /// confirmation, not a hole to store.
    /// </remarks>
    public static void ValidateCoreNutrients(
        decimal energyKcal,
        decimal fatG,
        decimal carbohydrateG,
        decimal proteinG,
        ValidationErrors errors)
    {
        if (energyKcal < 0)
        {
            errors.Add("energyKcal", "Energy cannot be negative.");
        }

        if (fatG < 0)
        {
            errors.Add("fatG", "Fat cannot be negative.");
        }

        if (carbohydrateG < 0)
        {
            errors.Add("carbohydrateG", "Carbohydrate cannot be negative.");
        }

        if (proteinG < 0)
        {
            errors.Add("proteinG", "Protein cannot be negative.");
        }
    }

    /// <summary>Normalises a barcode, or reports why it is not one.</summary>
    /// <remarks>
    /// Digits only. Barcodes are never typed by a user (CLAUDE.md section 1 keeps them invisible),
    /// so anything else here means a caller is putting something in the field that is not a
    /// barcode - and that field is a uniqueness key for the whole household's catalog.
    /// </remarks>
    /// <param name="field">
    /// Which field to report a bad barcode against. Defaults to the catalog's, because a log entry
    /// carries several items and has to say which one.
    /// </param>
    /// <summary>What the <c>Barcode</c> column holds.</summary>
    private const int MaxBarcodeLength = 32;

    /// <summary>What the <c>IngredientsText</c> column holds.</summary>
    private const int MaxIngredientsLength = 4000;

    /// <summary>Longer than any tag Open Food Facts publishes, and short of anything alarming.</summary>
    private const int MaxTagLength = 64;

    /// <summary>A sanity limit rather than a considered ceiling on how many allergens a food has.</summary>
    private const int MaxTags = 50;

    /// <summary>
    /// Checks and tidies what a product is made of.
    /// </summary>
    /// <remarks>
    /// <strong>An ingredient list is only true of a specific product</strong>, so it is accepted
    /// only on an item carrying a brand or a barcode. "Chicken breast" has no formulation to
    /// describe, and a shared catalog where any account may edit any global item is exactly the
    /// wrong place for one account's guess at what a generic food contains.
    /// <para>
    /// A recipe is refused outright: it derives its own from its components, the same way it derives
    /// its nutrition, so a stored list would be a second version of the same fact free to disagree
    /// with the first.
    /// </para>
    /// </remarks>
    /// <param name="isRecipe">Whether the item being saved has components.</param>
    /// <param name="isSpecificProduct">Whether it carries a brand or a barcode.</param>
    public static (string? Text, List<string> Allergens, List<string> DietFlags) NormaliseIngredients(
        string? ingredientsText,
        IReadOnlyCollection<string> allergens,
        IReadOnlyCollection<string> dietFlags,
        bool isRecipe,
        bool isSpecificProduct,
        ValidationErrors errors)
    {
        var text = string.IsNullOrWhiteSpace(ingredientsText) ? null : ingredientsText.Trim();
        var anything = text is not null || allergens.Count > 0 || dietFlags.Count > 0;

        if (anything && isRecipe)
        {
            errors.Add(
                "ingredientsText",
                "A recipe's ingredients come from the items it is made of, so it cannot be given a "
                    + "list of its own.");

            return (null, [], []);
        }

        if (anything && !isSpecificProduct)
        {
            errors.Add(
                "ingredientsText",
                "An ingredient list describes one brand's product, so it needs a brand or a barcode "
                    + "to belong to.");

            return (null, [], []);
        }

        if (text?.Length > MaxIngredientsLength)
        {
            errors.Add(
                "ingredientsText",
                $"That ingredient list is too long ({MaxIngredientsLength} characters at most).");

            text = null;
        }

        return (text, NormaliseTags(allergens, "allergens", errors), NormaliseTags(dietFlags, "dietFlags", errors));
    }

    /// <summary>
    /// Trims, lowercases and de-duplicates a tag list, keeping the order it arrived in.
    /// </summary>
    /// <remarks>
    /// Lowercased because these are Open Food Facts identifiers rather than prose, and two spellings
    /// of <c>en:milk</c> that differ only in case would be two allergens as far as a query is
    /// concerned. Not validated against a vocabulary: OFF's taxonomy is theirs and grows without
    /// asking, and refusing a tag this server has not heard of would drop a real allergen warning.
    /// </remarks>
    private static List<string> NormaliseTags(
        IReadOnlyCollection<string> tags,
        string field,
        ValidationErrors errors)
    {
        if (tags.Count > MaxTags)
        {
            errors.Add(field, $"That is more than {MaxTags} tags, which is more than a food has.");

            return [];
        }

        var kept = new List<string>(tags.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var trimmed = tag.Trim().ToLowerInvariant();

            if (trimmed.Length > MaxTagLength)
            {
                errors.Add(field, $"A tag is at most {MaxTagLength} characters.");

                return [];
            }

            if (seen.Add(trimmed))
            {
                kept.Add(trimmed);
            }
        }

        return kept;
    }

    public static string? NormaliseBarcode(string? barcode, ValidationErrors errors, string field = "barcode")
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var trimmed = barcode.Trim();

        if (!trimmed.All(char.IsAsciiDigit))
        {
            errors.Add(field, "A barcode is digits only.");
            return null;
        }

        // The column is varchar(32) and the StringLength attribute on the DTO is documentation -
        // nothing calls AddValidation - so without this a long barcode reaches Postgres and comes
        // back as a 500. The longest real one is 14 digits; 32 is already generous.
        if (trimmed.Length > MaxBarcodeLength)
        {
            errors.Add(field, $"That barcode is too long ({MaxBarcodeLength} digits at most).");
            return null;
        }

        return trimmed;
    }
}
