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
    public static string? NormaliseBarcode(string? barcode, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var trimmed = barcode.Trim();

        if (!trimmed.All(char.IsAsciiDigit))
        {
            errors.Add("barcode", "A barcode is digits only.");
            return null;
        }

        return trimmed;
    }
}
