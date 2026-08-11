using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Runs the three stages of CLAUDE.md section 5 and decides whose numbers win.
/// </summary>
/// <remarks>
/// Barcode, then Open Food Facts, then the model - each only doing work the one before it did not
/// already do. The shape of it is in section 5; what this class adds is the two rules that section
/// leaves implicit and that decide whether the answer is any good.
/// <list type="number">
/// <item><description>
/// <strong>A fully matched product's photograph never reaches the model</strong>, and its numbers
/// are used verbatim. That is section 5's token and accuracy win, and it is enforced in
/// <see cref="MealPrompt.PhotosToSend"/> so it is visible in the request that goes out.
/// </description></item>
/// <item><description>
/// <strong>Where the database and the model both have a figure, the database wins.</strong> One was
/// read off a printed label by a project with two decades of corrections behind it; the other was
/// guessed from a photograph by a model small enough to run on a home server. The model's job is to
/// say which product and how much of it.
/// </description></item>
/// </list>
/// <para>
/// It writes nothing. Confirm-before-save (section 2) means the only thing that turns any of this
/// into a row is a person tapping confirm, which is milestone 9.
/// </para>
/// </remarks>
public sealed class MealCascade(
    IBarcodeDecoder decoder,
    IProductLookup lookup,
    IMealAnalyzer analyzer,
    ILogger<MealCascade> logger)
{
    public async Task<MealAnalysisResult> AnalyzeAsync(
        string? text,
        IReadOnlyList<MealPhoto> photos,
        CancellationToken cancellationToken)
    {
        // Seeded before the model is called, and that ordering is the point. Section 5 requires a
        // stage failure to reach the user "regardless of what the AI says", so a list built on the
        // success path would lose the Open Food Facts warnings the moment Ollama also failed - and
        // "the AI is unavailable" without "...and the food database was rate-limiting us" is a
        // materially less useful thing to be told.
        var warnings = new List<string>();
        var known = new List<KnownProduct>();

        foreach (var photo in photos)
        {
            await ExamineAsync(photo, known, warnings, cancellationToken);
        }

        var request = new MealAnalysisRequest(text, known, photos, Distinct(warnings));
        var reading = await analyzer.AnalyzeAsync(request, cancellationToken);

        warnings.AddRange(reading.Warnings);

        return reading.Succeeded
            ? Merge(reading, known, warnings)
            : WithoutTheModel(reading, known, warnings);
    }

    /// <summary>Stages one and two, for one photograph.</summary>
    private async Task ExamineAsync(
        MealPhoto photo,
        List<KnownProduct> known,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var decoded = decoder.Decode(photo.Content);

        if (decoded.Problem is { } problem)
        {
            warnings.Add(problem);
        }

        if (!decoded.HasBarcode)
        {
            // The ordinary case. Most meals are not packaged, and a photo with no barcode in it is
            // simply a photo for the model to look at.
            return;
        }

        var result = await lookup.FindByBarcodeAsync(decoded.Barcode!, cancellationToken);

        warnings.AddRange(result.Warnings);

        if (result.Product is null)
        {
            return;
        }

        var complete = result.Outcome is ProductLookupOutcome.Matched;

        known.Add(new KnownProduct(
            MealPrompt.Reference(known.Count), photo.Id, complete, result.Product));

        logger.LogDebug(
            "A photo resolved to a {Outcome} product, so its image {Fate} be sent to the model.",
            result.Outcome,
            complete ? "will not" : "will");
    }

    /// <summary>
    /// Combines what the model said with what the database knew.
    /// </summary>
    /// <remarks>
    /// The serving is never re-based here, and that is deliberate. The prompt pins each identified
    /// product's serving and asks for the amount as a fraction of it, so both sides are already
    /// talking about the same quantity of food. Taking one side's protein and the other's fat would
    /// produce a card whose numbers each came from somewhere defensible and whose totals were
    /// nonsense - the failure docs/decisions/08-barcode-off.md had to solve once already, on the
    /// other side of the same seam.
    /// </remarks>
    private static MealAnalysisResult Merge(
        ModelReading reading,
        IReadOnlyList<KnownProduct> known,
        List<string> warnings)
    {
        var byReference = known.ToDictionary(product => product.Reference, StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<MealAnalysisItem>();

        foreach (var item in reading.Items)
        {
            if (item.ProductReference is { } reference && byReference.TryGetValue(reference, out var product))
            {
                used.Add(reference);
                items.Add(FromProduct(product, item));
                continue;
            }

            items.Add(FromModel(item));
        }

        // A product the barcode path fully identified, which the model then failed to mention. This
        // is a real outcome rather than a hypothetical: its photograph was deliberately withheld, so
        // the model is working from text alone and may well describe it as "crisps" with no link
        // back. Dropping it would discard the best data in the whole cascade in favour of a guess
        // made by a model that was never shown the picture.
        foreach (var product in known.Where(product => product.Complete && !used.Contains(product.Reference)))
        {
            items.Add(FromProduct(product, item: null));
            warnings.Add(
                $"{product.Draft.Name ?? "A scanned product"} was recognised from its barcode but the "
                    + "local model did not mention it, so one serving was assumed. Please check the "
                    + "amount.");
        }

        return items.Count == 0
            ? MealAnalysisResult.Failed(
                "Nothing could be read from what you sent.", reading.Note, Distinct(warnings))
            : MealAnalysisResult.Analyzed(items, reading.Note, Distinct(warnings));
    }

    /// <summary>
    /// What to report when the model failed but the barcode path did not.
    /// </summary>
    /// <remarks>
    /// Section 5 says an AI failure must never save a guessed or empty entry, and this saves
    /// nothing - but a fully matched product is not a guess. It is a label reading from a nutrition
    /// database, and throwing it away because a <em>different</em> stage failed contradicts the
    /// cascade's own logic: the whole reason that photograph was withheld is that its numbers were
    /// already better than anything the model would produce.
    /// <para>
    /// The quantity is the one thing genuinely unknown, so it is assumed to be one serving and said
    /// so.
    /// </para>
    /// </remarks>
    private static MealAnalysisResult WithoutTheModel(
        ModelReading reading,
        IReadOnlyList<KnownProduct> known,
        List<string> warnings)
    {
        var matched = known.Where(product => product.Complete).ToList();

        if (matched.Count == 0)
        {
            return MealAnalysisResult.Failed(reading.Failure!, reading.Note, Distinct(warnings));
        }

        warnings.Add(reading.Failure!);
        warnings.Add(
            "The barcode was recognised, so these figures come from the food database rather than "
                + "from the photo. The amount is assumed to be one serving.");

        return MealAnalysisResult.Analyzed(
            [.. matched.Select(product => FromProduct(product, item: null))],
            reading.Note,
            Distinct(warnings));
    }

    /// <summary>
    /// One item whose identity came from the database.
    /// </summary>
    /// <param name="item">
    /// What the model said about it, or null when it said nothing - a withheld photograph the model
    /// never connected to anything, or a model failure. Only the quantity is ever taken from here
    /// for a full match.
    /// </param>
    private static MealAnalysisItem FromProduct(KnownProduct product, ModelItem? item)
    {
        var draft = product.Draft;
        var complete = product.Complete;

        // A partial match is the only case where the model contributes numbers, and it contributes
        // them only where the database had none. "Missing is not zero" is what makes this legible:
        // a null on the draft means Open Food Facts did not know, so there is a hole to fill, while
        // a zero means it said zero and there is not.
        var nutrients = new Dictionary<string, decimal>(draft.Nutrients, StringComparer.Ordinal);

        if (!complete && item is not null)
        {
            foreach (var (key, amount) in item.Nutrients)
            {
                nutrients.TryAdd(key, amount);
            }
        }

        return new MealAnalysisItem(
            Name: draft.Name ?? item?.Name ?? "Unnamed product",
            Brand: draft.Brand ?? (complete ? null : item?.Brand),
            Barcode: draft.Barcode,
            MealImageId: product.MealImageId,
            Source: complete ? AnalyzedItemSource.Database : AnalyzedItemSource.DatabaseAndModel,

            // A full match's figures are the database's throughout, so nothing the model said about
            // them can be inconsistent. A partial match keeps the model's verdict, because the
            // fields it filled in are exactly the ones that were checked.
            Confidence: complete ? AnalysisConfidence.Normal : item?.Confidence ?? AnalysisConfidence.Normal,
            Quantity: item?.Quantity ?? 1m,

            // Never the model's serving, even on a partial match. The prompt pinned this one and
            // asked for the amount as a fraction of it; letting the model redefine it afterwards
            // would silently re-scale every figure the database supplied.
            ServingSize: draft.ServingSize,
            ServingUnit: draft.ServingUnit,
            EnergyKcal: draft.EnergyKcal ?? item?.EnergyKcal ?? 0m,
            FatG: draft.FatG ?? item?.FatG ?? 0m,
            CarbohydrateG: draft.CarbohydrateG ?? item?.CarbohydrateG ?? 0m,
            ProteinG: draft.ProteinG ?? item?.ProteinG ?? 0m,
            Nutrients: nutrients,

            // A full match's warnings are dropped, and this is not tidying. The reader's complaints
            // are about the figures the model produced, and every one of those has just been
            // replaced by the database's - so keeping them would put "these numbers do not add up"
            // on a card whose numbers are fine and are not the ones being described. A partial match
            // keeps them, because there its figures partly survived.
            Warnings: complete ? [] : item?.Warnings ?? []);
    }

    private static MealAnalysisItem FromModel(ModelItem item) =>
        new(
            Name: item.Name,
            Brand: item.Brand,
            Barcode: null,
            MealImageId: null,
            Source: AnalyzedItemSource.Model,
            Confidence: item.Confidence,
            Quantity: item.Quantity,
            ServingSize: item.ServingSize,
            ServingUnit: item.ServingUnit,
            EnergyKcal: item.EnergyKcal,
            FatG: item.FatG,
            CarbohydrateG: item.CarbohydrateG,
            ProteinG: item.ProteinG,
            Nutrients: item.Nutrients,
            Warnings: item.Warnings);

    /// <summary>
    /// Drops repeats while keeping the order.
    /// </summary>
    /// <remarks>
    /// Four photographs of the same shelf produce four identical rate-limit warnings, and a card
    /// that says the same thing four times reads like four problems.
    /// </remarks>
    private static IReadOnlyList<string> Distinct(IEnumerable<string> warnings) =>
        [.. warnings.Distinct(StringComparer.Ordinal)];
}
