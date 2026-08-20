using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Filing a confirmed meal's scanned products into the catalog - milestone 10.
/// </summary>
/// <remarks>
/// Confirming a meal is the only moment the server learns both what a product is and that a person
/// believes the numbers, so it is the moment worth writing one down. Four rules decide what happens,
/// and each of them is a decision recorded in <c>docs/decisions/12-catalog-growth.md</c> rather than
/// an implementation detail.
/// <list type="number">
/// <item><description>
/// <strong>A barcode is the only key.</strong> An item without one files nothing. Matching on name
/// would be wrong whichever way it went: "chicken breast" at 165 kcal and at 195 kcal are two
/// readings of two different foods, so a hit merges things that are not the same, and a miss writes
/// a near-duplicate row every meal.
/// </description></item>
/// <item><description>
/// <strong>Everything written here is personal.</strong> A global item cannot be deleted, which
/// makes automatic promotion the one irreversible thing the catalog does - and doing it unasked
/// would also tell the household what each of its members eats.
/// <c>POST /api/foods/{id}/share</c> stays the deliberate act.
/// </description></item>
/// <item><description>
/// <strong>A hit links and writes nothing.</strong> Tapping save consents to logging a meal, not to
/// editing a catalog row; corrections belong to <c>PUT /api/foods/{id}</c>, where they are
/// attributed. This is also what stops a personal portion adjustment from rewriting a label.
/// </description></item>
/// <item><description>
/// <strong>A personal row beats a shared one.</strong> It exists precisely because its owner
/// disagreed with the shared figures, and preferring the shared row would overrule them silently.
/// A shared hit is used as it stands and never copied, which is how CLAUDE.md section 7's
/// "no per-user duplicates of a shared product" is honoured.
/// </description></item>
/// </list>
/// </remarks>
internal static class CatalogUpsert
{
    /// <summary>
    /// Resolves every barcode in the request to a catalog item, creating what is missing.
    /// </summary>
    /// <returns>Barcode to catalog item id, for the barcodes that resolved.</returns>
    /// <remarks>
    /// Saves the new rows itself rather than riding the caller's <c>SaveChanges</c>, and the caller
    /// is expected to swallow a failure here. The meal is what the user confirmed and the catalog
    /// row is a convenience; losing the meal to a failed convenience would be the wrong trade. The
    /// converse - a catalog row for a meal that then failed to save - costs nothing, because the
    /// retry finds that row and links it rather than colliding with it.
    /// </remarks>
    public static async Task<IReadOnlyDictionary<string, Guid>> FileAsync(
        TrackrDbContext db,
        Guid userId,
        SaveLogEntryRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var fileable = request.Items
            .Where(CanFile)
            .GroupBy(item => item.Barcode!.Trim(), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        if (fileable.Count == 0)
        {
            return ReadOnlyDictionary.Empty;
        }

        var barcodes = fileable.Keys.ToList();
        var resolved = await ResolveAsync(db, userId, barcodes, cancellationToken);

        var added = new List<FoodItem>();

        foreach (var (barcode, item) in fileable)
        {
            if (resolved.ContainsKey(barcode))
            {
                continue;
            }

            var created = NewFrom(item, barcode, userId, now);

            db.FoodItems.Add(created);
            added.Add(created);

            resolved[barcode] = created.Id;
        }

        if (added.Count == 0)
        {
            return resolved;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two devices on one account confirming the same product at the same moment. The partial
            // unique index is what actually enforces one row per account per barcode, so the loser
            // drops what it built and takes what the winner wrote - which is the same answer, and
            // reached without the meal noticing.
            foreach (var item in added)
            {
                db.Entry(item).State = EntityState.Detached;
            }

            return await ResolveAsync(db, userId, barcodes, cancellationToken);
        }

        return resolved;
    }

    /// <summary>
    /// What is worth filing: a barcode, and a serving to hang the numbers on.
    /// </summary>
    /// <remarks>
    /// <c>FoodItem.ServingSize</c> and <c>ServingUnit</c> are not nullable, and inventing "one
    /// serving" for an item nobody measured would store a measurement that was never taken. An item
    /// like that is logged exactly as before; it just does not become a catalog row.
    /// </remarks>
    private static bool CanFile(SaveLogItemRequest item) =>
        !string.IsNullOrWhiteSpace(item.Barcode)
        && item.ServingSize is > 0
        && !string.IsNullOrWhiteSpace(item.ServingUnit);

    /// <summary>
    /// The caller's own row for each barcode, falling back to the shared one.
    /// </summary>
    private static async Task<Dictionary<string, Guid>> ResolveAsync(
        TrackrDbContext db,
        Guid userId,
        List<string> barcodes,
        CancellationToken cancellationToken)
    {
        var candidates = await CatalogItems.VisibleTo(db, userId)
            .Where(item => item.Barcode != null && barcodes.Contains(item.Barcode))
            .Select(item => new { item.Id, item.Barcode, item.UserId })
            .ToListAsync(cancellationToken);

        return candidates
            .GroupBy(candidate => candidate.Barcode!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                // Personal first: a row exists here because its owner disagreed with the shared one.
                group => group.OrderBy(candidate => candidate.UserId is null ? 1 : 0).First().Id,
                StringComparer.Ordinal);
    }

    /// <summary>
    /// A personal catalog row holding exactly what the user confirmed.
    /// </summary>
    /// <remarks>
    /// From the request rather than from the analysis it came out of, because the numbers on the
    /// card are the ones a person looked at. Building it from the pre-edit reading would put a
    /// figure in the catalog that nobody ever approved, which inverts confirm-before-save.
    /// <para>
    /// The source is <see cref="FoodSource.Off"/> because only stage one of the cascade produces a
    /// barcode. That is true even where the model filled gaps the database left, since the identity
    /// still came off the label.
    /// </para>
    /// </remarks>
    private static FoodItem NewFrom(SaveLogItemRequest item, string barcode, Guid userId, DateTimeOffset now)
    {
        var created = CatalogItems.New(
            name: item.Name.Trim(),
            brand: string.IsNullOrWhiteSpace(item.Brand) ? null : item.Brand.Trim(),
            barcode: barcode,
            servingSize: item.ServingSize!.Value,
            servingUnit: item.ServingUnit!.Trim(),
            source: FoodSource.Off,
            owner: userId,
            editorId: userId,
            now: now);

        // Per serving on both sides, so this is a copy rather than a conversion. The quantity is the
        // log's business and is not stored on a catalog row at all.
        CatalogItems.ApplyNutrition(
            created,
            item.EnergyKcal,
            item.FatG,
            item.CarbohydrateG,
            item.ProteinG,
            item.Nutrients);

        // Milestone 10a. Safe without the brand-or-barcode check the catalog endpoints run: this
        // path only ever reaches here for an item that has a barcode.
        CatalogItems.ApplyIngredients(created, item.IngredientsText, item.Allergens, item.DietFlags);

        return created;
    }

    private static class ReadOnlyDictionary
    {
        public static readonly IReadOnlyDictionary<string, Guid> Empty =
            new Dictionary<string, Guid>(StringComparer.Ordinal);
    }
}
