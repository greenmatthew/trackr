using Trackr.Mobile.Core.Api;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.Nutrition;

/// <summary>
/// The server's nutrient vocabulary, fetched once and kept for the life of the process.
/// </summary>
/// <remarks>
/// A confirmation card holds amounts keyed by nutrient - <c>vitamin_c</c>, <c>sodium</c> - and
/// nothing else. Turning those into "Vitamin C 12 mg" needs the display name, the unit and the
/// label order, all of which the server owns: adding a nutrient there is a data change
/// (wiki/Nutrient-Reference.md), so a list hardcoded here would render a new one against the wrong
/// name, or in grams when it is micrograms.
/// <para>
/// Cached because the answer changes about once a year and the card is drawn on every meal.
/// In memory rather than in the phone's SQLite database, deliberately: what has to survive a
/// restart is a meal the server has not taken (<c>Storage/LogOutbox</c>), and a table here would be
/// schema to maintain for a request that costs one round trip on the first meal of a session. The
/// consequence is worth stating: with no network and a cold process, a card shows its calories and
/// macros and leaves the micronutrient rows out, which is the degradation this class is built for.
/// </para>
/// </remarks>
public sealed class NutrientCatalogCache(ITrackrApiClient api)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    private IReadOnlyDictionary<string, NutrientResponse>? byKey;

    /// <summary>
    /// The catalog, or null when it has never been fetched successfully.
    /// </summary>
    /// <remarks>
    /// Null is a normal state, not an error: a card whose nutrient names could not be looked up
    /// still shows its calories and macros, which are typed fields and need no catalog. It just
    /// leaves out the micronutrient rows rather than labelling them with raw keys.
    /// </remarks>
    public IReadOnlyDictionary<string, NutrientResponse>? ByKey => byKey;

    /// <summary>
    /// Fetches the catalog if it is not already held. Safe to call before every analysis.
    /// </summary>
    /// <remarks>
    /// A failure is not cached - the next call tries again. The likely cause is that the phone was
    /// offline for a moment, and a session-long refusal to look at nutrients because of it would be
    /// a worse bug than the extra request.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, NutrientResponse>?> EnsureLoadedAsync(
        CancellationToken cancellationToken = default)
    {
        if (byKey is not null)
        {
            return byKey;
        }

        await gate.WaitAsync(cancellationToken);

        try
        {
            if (byKey is not null)
            {
                return byKey;
            }

            var nutrients = await api.GetNutrientsAsync(cancellationToken);
            if (nutrients is null)
            {
                return null;
            }

            byKey = nutrients.ToDictionary(nutrient => nutrient.Key, StringComparer.Ordinal);

            return byKey;
        }
        finally
        {
            gate.Release();
        }
    }
}
