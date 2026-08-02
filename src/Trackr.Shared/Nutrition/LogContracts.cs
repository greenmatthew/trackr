using System.ComponentModel.DataAnnotations;

namespace Trackr.Shared.Nutrition;

/// <summary>One logging occasion: what was eaten, when, and the photos that went with it.</summary>
/// <remarks>
/// The entry and its items are created in one request. An entry with no items is meaningless, and
/// a two-step create would orphan entries every time the second call failed.
/// </remarks>
public sealed class SaveLogEntryRequest
{
    /// <summary>
    /// When the food was eaten. Defaults to now if the client does not say.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the row's creation timestamp: correcting "I actually ate this at
    /// 8am" must move the entry between days without lying about when the row was written.
    /// </remarks>
    public DateTimeOffset? LoggedUtc { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>At least one. A request with none is a 400.</summary>
    public List<SaveLogItemRequest> Items { get; set; } = [];

    /// <summary>
    /// Photos to attach, by the id <c>POST /api/images</c> returned.
    /// </summary>
    /// <remarks>
    /// Images are uploaded before the entry exists - the chat flow is upload, then cascade, then
    /// confirm - so the entry adopts them rather than carrying their bytes. Each id must belong to
    /// the caller and not already be attached to another entry.
    /// </remarks>
    public List<Guid> ImageIds { get; set; } = [];
}

/// <summary>One food inside a log entry.</summary>
/// <remarks>
/// <strong>Nutrient values here are per serving, and the server multiplies them by
/// <see cref="Quantity"/> before storing.</strong> The stored snapshot is therefore a set of
/// totals, which is what makes milestone 11's aggregate a plain <c>SUM</c> and what lets an ad-hoc
/// item ("a bowl of chili, 480 kcal") exist without inventing a serving size. The arithmetic is
/// exact decimal, so the totals the confirmation card showed are the totals stored.
/// <para>
/// <see cref="FoodItemId"/> is provenance only. The server never joins through it to find values -
/// everything it stores comes from this request - which is what stops a later correction to a
/// shared catalog item from rewriting numbers somebody already confirmed.
/// </para>
/// </remarks>
public sealed class SaveLogItemRequest
{
    /// <summary>
    /// The catalog item this came from, if any. Must be visible to the caller: their own, or global.
    /// </summary>
    public Guid? FoodItemId { get; set; }

    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(120)]
    public string? Brand { get; set; }

    /// <summary>How many servings. Must be greater than zero; 2.5 is fine.</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>What one serving was, recorded so the entry still reads sensibly later. Null for ad-hoc items.</summary>
    public decimal? ServingSize { get; set; }

    [StringLength(32)]
    public string? ServingUnit { get; set; }

    public decimal EnergyKcal { get; set; }
    public decimal FatG { get; set; }
    public decimal CarbohydrateG { get; set; }
    public decimal ProteinG { get; set; }

    /// <summary>Per-serving amounts for every nutrient except the core four. Same rules as the catalog.</summary>
    public Dictionary<string, decimal> Nutrients { get; set; } = new(StringComparer.Ordinal);
}

/// <param name="Items">Never empty.</param>
/// <param name="Images">Metadata only - fetch the bytes from <c>GET /api/images/{id}</c>.</param>
public sealed record LogEntryResponse(
    Guid Id,
    DateTimeOffset LoggedUtc,
    string? Note,
    IReadOnlyList<LogItemResponse> Items,
    IReadOnlyList<MealImageResponse> Images,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

/// <summary>A frozen snapshot of one food, as it was when the user confirmed it.</summary>
/// <param name="FoodItemId">
/// Where it came from, or null - either because it was ad-hoc, or because the catalog item has
/// since been deleted. The numbers below are unaffected either way.
/// </param>
/// <param name="EnergyKcal">Total for <paramref name="Quantity"/> servings, not per serving.</param>
/// <param name="Nutrients">
/// Totals for <paramref name="Quantity"/> servings, keyed as the nutrient catalog reports, and
/// never containing the core four.
/// </param>
public sealed record LogItemResponse(
    Guid Id,
    Guid? FoodItemId,
    string Name,
    string? Brand,
    decimal Quantity,
    decimal? ServingSize,
    string? ServingUnit,
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    IReadOnlyDictionary<string, decimal> Nutrients);
