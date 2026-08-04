using System.Text.Json.Serialization;

namespace Trackr.Shared.Nutrition;

/// <summary>Which of CLAUDE.md section 5's four branches a barcode lookup took.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductLookupOutcome>))]
public enum ProductLookupOutcome
{
    /// <summary>
    /// Found, with a name and all four core nutrients. The one case where the photo is
    /// <strong>not</strong> sent to the model - the token and accuracy win section 5 calls out.
    /// </summary>
    Matched,

    /// <summary>
    /// Found, but missing the name or one of the core nutrients. The photo still goes to the model,
    /// alongside whatever was found, so it can fill the gaps.
    /// </summary>
    Partial,

    /// <summary>Not in the database. The photo goes to the model on its own.</summary>
    NotFound,

    /// <summary>
    /// The lookup could not be completed - rate limited, timed out, unreachable, unreadable. Not the
    /// same as <see cref="NotFound"/>: nothing was learned about the product, so the user is owed a
    /// warning saying so.
    /// </summary>
    Failed
}

/// <summary>
/// Where a draft's per-serving numbers came from, which decides how much to trust the serving size.
/// </summary>
/// <remarks>
/// Carried to the client rather than discarded because the assumption is invisible in the numbers
/// themselves. A user shown "1 serving = 100 g" for a jar of spread deserves to know that nobody
/// said so - the database simply had no serving size, and the model or the user is better placed to
/// guess.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ServingBasis>))]
public enum ServingBasis
{
    /// <summary>The database had per-serving figures and a serving size to go with them.</summary>
    LabelServing,

    /// <summary>Per-100 figures, scaled by a serving size the database did have.</summary>
    ScaledFromReferenceQuantity,

    /// <summary>
    /// Per-100 figures with no serving size anywhere, so one serving is taken to be the reference
    /// quantity itself - 100 g, or 100 ml for a drink.
    /// </summary>
    ReferenceQuantityAsServing
}

/// <summary>
/// A catalog item as a nutrition database described it, before anyone has confirmed it.
/// </summary>
/// <remarks>
/// Deliberately <em>not</em> <see cref="SaveFoodItemRequest"/>, though it is nearly the same shape.
/// That type's core four are non-nullable <c>decimal</c>, which is right for something being written
/// and wrong for something being reported: a product whose protein Open Food Facts does not know
/// would arrive as <c>0</c>, and "zero protein" is a claim nobody made. Nothing becomes a
/// <see cref="SaveFoodItemRequest"/> until the user has confirmed it (CLAUDE.md section 2).
/// <para>
/// <strong>Every value here is per one serving</strong>, and every amount is in its nutrient's own
/// unit as <c>GET /api/nutrients</c> reports it - not in the units the source happened to use.
/// </para>
/// </remarks>
/// <param name="Nutrients">
/// Everything except the core four, keyed as the nutrient catalog keys it. Absent means the source
/// had no value; <c>0</c> means it said zero. Never contains a core nutrient.
/// </param>
public sealed record ProductDraft(
    string Barcode,
    string? Name,
    string? Brand,
    decimal ServingSize,
    string ServingUnit,
    ServingBasis ServingBasis,
    decimal? EnergyKcal,
    decimal? FatG,
    decimal? CarbohydrateG,
    decimal? ProteinG,
    IReadOnlyDictionary<string, decimal> Nutrients);

/// <summary>
/// What a lookup learned, and anything the user should be told about how it went.
/// </summary>
/// <param name="Product">
/// The data found, or null for <see cref="ProductLookupOutcome.NotFound"/> and
/// <see cref="ProductLookupOutcome.Failed"/>. Present but incomplete for
/// <see cref="ProductLookupOutcome.Partial"/>.
/// </param>
/// <param name="MissingCoreFields">
/// For a partial match, which of <c>name</c>, <c>energy</c>, <c>fat</c>, <c>carbohydrate</c> and
/// <c>protein</c> the database did not have. Empty otherwise. Named so it can go straight into the
/// model's prompt as the list of things it is being asked to determine.
/// </param>
/// <param name="Warnings">
/// Plain-language notes for the user, empty on a clean match. Section 5's error rule in one field: a
/// rate limit, a timeout or an assumed serving size all end up here, and the confirmation card shows
/// them whatever the model says in its reply.
/// </param>
public sealed record ProductLookupResult(
    ProductLookupOutcome Outcome,
    ProductDraft? Product,
    IReadOnlyList<string> MissingCoreFields,
    IReadOnlyList<string> Warnings)
{
    public static ProductLookupResult Matched(ProductDraft product, IReadOnlyList<string>? warnings = null) =>
        new(ProductLookupOutcome.Matched, product, [], warnings ?? []);

    public static ProductLookupResult Partial(
        ProductDraft product,
        IReadOnlyList<string> missingCoreFields,
        IReadOnlyList<string>? warnings = null) =>
        new(ProductLookupOutcome.Partial, product, missingCoreFields, warnings ?? []);

    public static ProductLookupResult NotFound(IReadOnlyList<string>? warnings = null) =>
        new(ProductLookupOutcome.NotFound, null, [], warnings ?? []);

    /// <param name="reason">
    /// Written for the user, not for a log file: it is shown on the confirmation card and handed to
    /// the model. "Open Food Facts is rate-limiting requests", not a stack trace.
    /// </param>
    public static ProductLookupResult Failed(string reason) =>
        new(ProductLookupOutcome.Failed, null, [], [reason]);
}

/// <summary>
/// The result of examining an uploaded photo: what barcode was found in it, if any, and what the
/// nutrition database said about it.
/// </summary>
/// <remarks>
/// Stages one and two of the cascade, and no further - nothing here reaches the model, and nothing
/// here writes to the database. Milestone 8 adds the model; the confirmation card in milestone 9 is
/// what turns any of this into a saved entry.
/// <para>
/// <see cref="Barcode"/> is reported even though CLAUDE.md section 1 keeps barcodes invisible to the
/// user, because a client is not a user: the app needs it to decide whether to send the photo on to
/// the model. It is not for display.
/// </para>
/// </remarks>
public sealed record BarcodeScanResult(string? Barcode, ProductLookupResult Lookup);
