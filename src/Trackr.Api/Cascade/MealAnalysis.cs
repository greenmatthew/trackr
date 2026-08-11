using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Stage three of the cascade: ask a vision model what the user ate.
/// </summary>
/// <remarks>
/// Behind an interface for CLAUDE.md section 2's reason - each stage must be swappable - and section
/// 12 names the specific swap this anticipates: a hosted model as a lower-confidence backstop for
/// photographs the local one struggles with. Nothing downstream should learn which answered.
/// <para>
/// <strong>Implementations must not throw.</strong> Every failure is a
/// <see cref="ModelReading.Failed"/> carrying a sentence written for the user, because section 5
/// requires the reason to travel: onto the confirmation card, so somebody can judge how much to
/// trust what they are looking at. An exception escaping here would abandon a whole log attempt over
/// a stage that is allowed to fail.
/// </para>
/// <para>
/// The one exception is cancellation. If the <em>caller</em> gave up there is no failure to report
/// to anybody, so an <see cref="OperationCanceledException"/> from the caller's own token
/// propagates - the same contract, and the same reasoning, as <see cref="IProductLookup"/>.
/// </para>
/// </remarks>
public interface IMealAnalyzer
{
    Task<ModelReading> AnalyzeAsync(MealAnalysisRequest request, CancellationToken cancellationToken);
}

/// <summary>Everything stages one and two learned, assembled for the model.</summary>
/// <remarks>
/// This is CLAUDE.md section 5's assembly step as a type. The interesting part is what is
/// <em>absent</em>: a photograph whose product was fully identified is still listed in
/// <see cref="Photos"/> but is never sent, because <see cref="KnownProducts"/> already carries
/// better numbers than the model could read off it. <c>MealPrompt</c> is where that is decided, so
/// the decision is visible in the request that actually goes out rather than in an earlier caller's
/// filtering.
/// </remarks>
/// <param name="Text">What the user typed, if anything.</param>
/// <param name="KnownProducts">
/// Products Open Food Facts resolved, in the order they should be presented. Each is given a short
/// reference the model quotes back, which is how an item is tied to a product without the model
/// having to restate its numbers.
/// </param>
/// <param name="Photos">
/// Every photo attached to the request, complete matches included. The prompt decides which are sent.
/// </param>
/// <param name="EarlierProblems">
/// Anything that went wrong in stages one and two, phrased for a person. Section 5 asks for these to
/// reach the model so it can explain itself in plain language - "I couldn't reach the food database,
/// so I estimated from your photo instead" - and they reach the user separately regardless.
/// </param>
public sealed record MealAnalysisRequest(
    string? Text,
    IReadOnlyList<KnownProduct> KnownProducts,
    IReadOnlyList<MealPhoto> Photos,
    IReadOnlyList<string> EarlierProblems);

/// <summary>A product the barcode path identified, and how completely.</summary>
/// <param name="Reference">
/// A short handle - <c>p1</c>, <c>p2</c> - that appears in the prompt and comes back on the item the
/// model believes is this product. Short because it is generated and matched, never read by anybody.
/// </param>
/// <param name="MealImageId">
/// The photo it was found in. When <paramref name="Complete"/> is true this is the photo that will
/// <em>not</em> be sent.
/// </param>
/// <param name="Complete">
/// True for a full match - name and all four core nutrients. False for a partial one, whose photo
/// still goes to the model alongside what was found, so the gaps can be filled.
/// </param>
public sealed record KnownProduct(string Reference, Guid? MealImageId, bool Complete, ProductDraft Draft);

/// <summary>One uploaded photo, as stored.</summary>
/// <remarks>
/// The bytes are the original. Resizing for the model happens on a copy inside the analyzer, where
/// the context budget that makes it necessary is also known.
/// </remarks>
public sealed record MealPhoto(Guid Id, string ContentType, byte[] Content);

/// <summary>What the model said, once it has been parsed and checked.</summary>
/// <remarks>
/// Deliberately <em>not</em> <see cref="MealAnalysisResult"/>. This is one stage's opinion; the
/// cascade still has to decide whose numbers win where Open Food Facts also had some, and that merge
/// is the point at which a per-item source and the final warning list are settled. Keeping the two
/// types apart is what stops "what the model claimed" and "what the user will be shown" from
/// quietly becoming the same thing.
/// </remarks>
/// <param name="Note">
/// The model's own sentence about what it did or why it could not, when it offered one. Empty is
/// normal and means it had nothing to add.
/// </param>
/// <param name="Failure">
/// Null when the reading is usable. Otherwise a sentence for the user - unreachable, timed out,
/// unreadable answer - and <see cref="Items"/> is empty.
/// </param>
public sealed record ModelReading(
    string? Note,
    IReadOnlyList<ModelItem> Items,
    IReadOnlyList<string> Warnings,
    string? Failure)
{
    public static ModelReading Read(
        IReadOnlyList<ModelItem> items,
        string? note = null,
        IReadOnlyList<string>? warnings = null) =>
        new(note, items, warnings ?? [], null);

    public static ModelReading Failed(
        string reason,
        string? note = null,
        IReadOnlyList<string>? warnings = null) =>
        new(note, [], warnings ?? [], reason);

    public bool Succeeded => Failure is null;
}

/// <summary>One food the model reported, per serving, after validation.</summary>
/// <remarks>
/// Values are per one serving and <see cref="Quantity"/> is how many - the model is never asked to
/// multiply. Anything the validator could not trust has already been removed: a nutrient it dropped
/// is absent here, not zeroed.
/// </remarks>
/// <param name="ProductReference">
/// Which of the request's <see cref="KnownProduct"/> handles the model tied this item to, or null
/// when it said none of them.
/// </param>
public sealed record ModelItem(
    string? ProductReference,
    string Name,
    string? Brand,
    decimal Quantity,
    decimal? ServingSize,
    string? ServingUnit,
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    IReadOnlyDictionary<string, decimal> Nutrients,
    AnalysisConfidence Confidence,
    IReadOnlyList<string> Warnings);
