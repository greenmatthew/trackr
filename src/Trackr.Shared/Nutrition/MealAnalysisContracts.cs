using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Trackr.Shared.Nutrition;

/// <summary>What the user is asking the cascade to make sense of.</summary>
/// <remarks>
/// Text, photos, or both. CLAUDE.md section 1 is the whole shape of this type: the user types what
/// they ate in plain language and optionally attaches a picture, and everything else - the barcode,
/// the database lookup, the model - happens behind it.
/// <para>
/// Photos arrive by id rather than by value because they are uploaded first: the chat flow is
/// upload, then analyse, then confirm, which is the same reason
/// <see cref="SaveLogEntryRequest.ImageIds"/> works that way. It also means a photo can be analysed
/// twice - after a correction, or by a better model later - without being sent again.
/// </para>
/// </remarks>
public sealed class AnalyzeMealRequest
{
    /// <summary>What the user typed. Optional if there is at least one photo.</summary>
    [StringLength(2000)]
    public string? Text { get; set; }

    /// <summary>
    /// Photos to look at, by the id <c>POST /api/images</c> returned. Each must belong to the caller.
    /// </summary>
    public List<Guid> ImageIds { get; set; } = [];
}

/// <summary>Whether the cascade produced something worth showing.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MealAnalysisOutcome>))]
public enum MealAnalysisOutcome
{
    /// <summary>At least one item came back. It still needs confirming before anything is saved.</summary>
    Analyzed,

    /// <summary>
    /// Nothing usable. The model was unreachable, its answer could not be trusted, or it said plainly
    /// that it could not tell. <see cref="MealAnalysisResult.Warnings"/> says which.
    /// </summary>
    /// <remarks>
    /// A failure, never an empty success. CLAUDE.md section 5 is explicit that a guessed or empty
    /// entry must never be saved, so there is no outcome here that means "we found nothing, carry
    /// on".
    /// </remarks>
    Failed
}

/// <summary>Where an item's numbers actually came from.</summary>
/// <remarks>
/// Carried to the client because it changes how much the numbers deserve to be trusted, and the
/// difference is invisible otherwise. Deliberately not <see cref="FoodSource"/>: that records what a
/// <em>catalog</em> item's provenance was and has no value for the mixed case, which is exactly the
/// case worth telling somebody about.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AnalyzedItemSource>))]
public enum AnalyzedItemSource
{
    /// <summary>
    /// Read off a label by Open Food Facts. The model was told the product's name and figures and
    /// never saw the photo - CLAUDE.md section 5's token and accuracy win.
    /// </summary>
    Database,

    /// <summary>
    /// Open Food Facts had the product but not all of it, so the model saw the photo as well and
    /// filled the gaps. Every field the database knew is the database's; only the holes are the
    /// model's.
    /// </summary>
    DatabaseAndModel,

    /// <summary>The model alone, from a photo or from what the user typed. An estimate.</summary>
    Model
}

/// <summary>How much the server thinks an item's numbers hold together.</summary>
/// <remarks>
/// Not a measure of how good the model is - it is the result of arithmetic the server can check
/// without knowing anything about the food. Section 5 asks for calories to be reconciled against the
/// macros and for anything wildly off to be flagged rather than presented as fact; this is that flag,
/// plus the other cross-checks in <c>MealAnalysisReader</c>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AnalysisConfidence>))]
public enum AnalysisConfidence
{
    /// <summary>Nothing inconsistent was found. Not a promise that the numbers are right.</summary>
    Normal,

    /// <summary>
    /// Something did not add up. The item is still returned - the user is the one who decides - but
    /// it should be shown as needing a look rather than as a fact.
    /// </summary>
    Low
}

/// <summary>One food the cascade thinks was eaten, ready to be confirmed or corrected.</summary>
/// <remarks>
/// <strong>Every nutrient value here is per one serving, and <see cref="Quantity"/> is how many
/// servings.</strong> This is deliberately the same shape as <see cref="SaveLogItemRequest"/>, so
/// milestone 9's confirmation card can hand a corrected item straight back to
/// <c>POST /api/log</c> without a mapping layer in between - a mapping layer being a second place
/// for a number to change. A test asserts the two do not drift apart.
/// <para>
/// <strong>The server, not the model, does the multiplication.</strong> The model is asked for one
/// serving and a count, because "two eggs" is a thing a small model can get right and
/// <c>2 x 78 kcal</c> is a thing it frequently cannot. CLAUDE.md section 5 describes the prompt
/// computing the serving math; this moves that one multiplication to where it cannot be wrong, and
/// <c>docs/decisions/10-ollama.md</c> records why.
/// </para>
/// </remarks>
/// <param name="Barcode">
/// The number stage one read off the photo, if it read one. Not for display - CLAUDE.md section 1
/// keeps barcodes invisible - but carried because milestone 10 upserts confirmed items into the
/// catalog, and without it that milestone would have to scan the photograph a second time to
/// recover something this request already knew.
/// </param>
/// <param name="MealImageId">Which photo this item came out of, or null if it came from the text.</param>
/// <param name="ServingSize">
/// What one serving is. Null when nobody could say, which is normal for a plate of food and is why
/// the matching field on <see cref="SaveLogItemRequest"/> is nullable too.
/// </param>
/// <param name="Nutrients">
/// Per-serving amounts for everything except the core four, in each nutrient's own catalog unit.
/// Absent means not measured. It never means zero - see wiki/Nutrient-Reference.md - and a model
/// that could not read a value is expected to leave it out rather than invent one.
/// </param>
/// <param name="Warnings">
/// What happened to this particular item: a dropped nutrient, an assumed quantity, a figure that
/// disagreed with itself. Distinct from <see cref="MealAnalysisResult.Warnings"/>, which is about
/// the request as a whole.
/// </param>
public sealed record MealAnalysisItem(
    string Name,
    string? Brand,
    string? Barcode,
    Guid? MealImageId,
    AnalyzedItemSource Source,
    AnalysisConfidence Confidence,
    decimal Quantity,
    decimal? ServingSize,
    string? ServingUnit,
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    IReadOnlyDictionary<string, decimal> Nutrients,
    IReadOnlyList<string> Warnings);

/// <summary>Everything one run of the cascade produced. Nothing here has been saved.</summary>
/// <remarks>
/// <strong>This endpoint writes nothing</strong>, which is the confirm-before-save rule of CLAUDE.md
/// section 2 expressed as an API shape rather than as a promise. The user sees these numbers, edits
/// them if they are wrong, and only then does <c>POST /api/log</c> happen.
/// </remarks>
/// <param name="Note">
/// The model's own sentence about what it did, when it has one to offer - "I could not read the
/// label, so I estimated from the photo". Section 5 asks for exactly this, and it is also what a
/// <see cref="MealAnalysisOutcome.Failed"/> result carries instead of items: the model saying why it
/// could not tell is far more use than the server saying it returned nothing.
/// </param>
/// <param name="Warnings">
/// Plain-language notes about the run, shown whatever the model says in <paramref name="Note"/>.
/// Section 5 requires this to be independent: a rate limit, a timeout or an assumed serving size
/// reaches the user even if the model's own reply never mentions it. Seeded from stages one and two
/// <em>before</em> the model is called, so a model failure cannot take the earlier warnings with it.
/// </param>
public sealed record MealAnalysisResult(
    MealAnalysisOutcome Outcome,
    string? Note,
    IReadOnlyList<MealAnalysisItem> Items,
    IReadOnlyList<string> Warnings)
{
    public static MealAnalysisResult Analyzed(
        IReadOnlyList<MealAnalysisItem> items,
        string? note = null,
        IReadOnlyList<string>? warnings = null) =>
        new(MealAnalysisOutcome.Analyzed, note, items, warnings ?? []);

    /// <param name="reason">
    /// Written for the user, not for a log file - it is what the chat shows. "The local model did not
    /// answer within 240 seconds", not a stack trace.
    /// </param>
    /// <param name="note">The model's own words, when it managed to produce any.</param>
    public static MealAnalysisResult Failed(
        string reason,
        string? note = null,
        IReadOnlyList<string>? warnings = null) =>
        new(
            MealAnalysisOutcome.Failed,
            note,
            [],
            warnings is null or [] ? [reason] : [.. warnings, reason]);
}
