using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Trackr.Shared.Nutrition;

/// <summary>Where a catalog item's numbers came from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FoodSource>))]
public enum FoodSource
{
    /// <summary>Open Food Facts, by barcode. Milestone 7.</summary>
    Off,

    /// <summary>Read from a photo or a description by the local model. Milestone 8.</summary>
    Ai,

    /// <summary>Typed in by a person.</summary>
    Manual
}

/// <summary>Who can see and use a catalog item.</summary>
/// <remarks>
/// Chosen at creation rather than derived from <see cref="FoodSource"/>. Deriving it would forbid
/// sharing a hand-typed household staple and force sharing a barcode scan of something private.
/// <para>
/// Promotion from <see cref="Personal"/> to <see cref="Global"/> is a one-way move
/// (<c>POST /api/foods/{id}/share</c>): another account may already be logging a global item, so
/// there is deliberately no unshare.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<FoodVisibility>))]
public enum FoodVisibility
{
    /// <summary>Only the owning account sees it.</summary>
    Personal,

    /// <summary>Every account on this server sees it, and any of them may correct it.</summary>
    Global
}

/// <remarks>
/// A mutable class rather than a positional record, for the reason given at the top of
/// <c>Auth/LoginContracts.cs</c>: Blazor's <c>EditForm</c> binding needs settable properties, and
/// milestone 9's MAUI confirmation card binds the same way.
/// <para>
/// Every nutrient value here is <strong>per one serving</strong>. Open Food Facts returns both
/// per-100g and per-serving figures; milestone 7's mapper converts, so that by the time anything
/// reaches this shape the invariant already holds.
/// </para>
/// </remarks>
public sealed class SaveFoodItemRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = "";

    [StringLength(120)]
    public string? Brand { get; set; }

    /// <summary>Digits only. Absent for anything that never came off a package.</summary>
    [StringLength(32)]
    public string? Barcode { get; set; }

    /// <summary>How much one serving is, in <see cref="ServingUnit"/>. Must be greater than zero.</summary>
    public decimal ServingSize { get; set; } = 1m;

    /// <summary>Free text on purpose: "g", "ml", "slice", "egg".</summary>
    [Required]
    [StringLength(32)]
    public string ServingUnit { get; set; } = "";

    public FoodSource Source { get; set; } = FoodSource.Manual;

    /// <summary>Defaults to personal. Sharing is the deliberate act, not the accident.</summary>
    public FoodVisibility Visibility { get; set; } = FoodVisibility.Personal;

    public decimal EnergyKcal { get; set; }
    public decimal FatG { get; set; }
    public decimal CarbohydrateG { get; set; }
    public decimal ProteinG { get; set; }

    /// <summary>
    /// Every other nutrient, keyed as <c>GET /api/nutrients</c> reports it, per serving.
    /// </summary>
    /// <remarks>
    /// Absence means "not measured" and <c>0</c> means "known to be zero" - the distinction the
    /// wiki calls out, and one impossible to lose in serialisation because there is no null to
    /// misread. The four core nutrients above must NOT appear here; sending one is a 400.
    /// </remarks>
    public Dictionary<string, decimal> Nutrients { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One catalog item, with everything known about it.</summary>
/// <param name="Visibility">Personal to the caller, or shared with the whole server.</param>
/// <param name="IsEditable">
/// Whether this caller may change it - "mine, or global". Computed server-side so the rule lives
/// in one place and a client only has to render it.
/// </param>
/// <param name="Nutrients">
/// Per-serving amounts for every nutrient except the core four, which are the typed properties
/// above. Summing this map and then adding those properties would double count.
/// </param>
/// <param name="UpdatedByUserId">
/// Who last edited it, for global items corrected wiki-style. Null if it has never been edited by
/// anyone but its creator, or if that account has since been deleted.
/// </param>
public sealed record FoodItemResponse(
    Guid Id,
    string Name,
    string? Brand,
    string? Barcode,
    decimal ServingSize,
    string ServingUnit,
    FoodSource Source,
    FoodVisibility Visibility,
    bool IsEditable,
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    IReadOnlyDictionary<string, decimal> Nutrients,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    Guid? UpdatedByUserId);

/// <summary>A catalog row for a list. No nutrient map - fetch the item for that.</summary>
public sealed record FoodItemSummaryResponse(
    Guid Id,
    string Name,
    string? Brand,
    string? Barcode,
    decimal ServingSize,
    string ServingUnit,
    FoodSource Source,
    FoodVisibility Visibility,
    bool IsEditable,
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    DateTimeOffset UpdatedUtc);
