namespace Trackr.Api.Data;

/// <summary>
/// One food inside a log entry, frozen as it was when the user confirmed it.
/// </summary>
/// <remarks>
/// This is CLAUDE.md section 7's snapshot rule made concrete: name, brand, serving and every
/// nutrient value are copied onto this row at logging time, so correcting a catalog item later -
/// or another household member correcting a shared one - never rewrites anybody's history.
/// <para>
/// <strong>The nutrient values here are totals for <see cref="Quantity"/> servings, not
/// per-serving amounts.</strong> Section 7 asks for the "full computed nutrient snapshot", and
/// computing it at write time buys three things: the number the user approved is literally the
/// number stored, an ad-hoc item ("a bowl of chili, 480 kcal") needs no invented serving size, and
/// milestone 11's aggregate is a plain <c>SUM</c>. The cost, stated honestly: milestone 14's
/// "change the quantity" edit has to rescale every nutrient row rather than update one column.
/// </para>
/// </remarks>
public class LogItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid LogEntryId { get; set; }

    public LogEntry? LogEntry { get; set; }

    /// <summary>
    /// Which catalog item this came from, if any. <strong>Provenance only.</strong>
    /// </summary>
    /// <remarks>
    /// Nothing may join through this foreign key to compute a total - every number on this row is
    /// already here. That rule is what keeps the shared catalog safe, and it is the thing most
    /// likely to be quietly reintroduced as a bug by a later "optimisation".
    /// <para>
    /// Nullable, and SetNull on delete. Cascade would let someone erase their own history by
    /// tidying their catalog, which is precisely the failure the snapshot rule exists to prevent;
    /// Restrict would make any logged item permanently undeletable. SetNull keeps the row and its
    /// numbers and loses only the back-link.
    /// </para>
    /// </remarks>
    public Guid? FoodItemId { get; set; }

    public FoodItem? FoodItem { get; set; }

    /// <summary>Copied from the catalog item, or typed for an ad-hoc entry.</summary>
    public string Name { get; set; } = "";

    public string? Brand { get; set; }

    /// <summary>How many servings were eaten. 2.5 is a perfectly ordinary value.</summary>
    public decimal Quantity { get; set; } = 1m;

    /// <summary>What one serving was, recorded so the entry still reads sensibly later.</summary>
    /// <remarks>Null for ad-hoc items, which have no serving to speak of.</remarks>
    public decimal? ServingSize { get; set; }

    /// <inheritdoc cref="ServingSize"/>
    public string? ServingUnit { get; set; }

    /// <summary>Total energy for <see cref="Quantity"/> servings.</summary>
    public decimal EnergyKcal { get; set; }

    /// <summary>Total fat for <see cref="Quantity"/> servings.</summary>
    public decimal FatG { get; set; }

    /// <summary>Total carbohydrate for <see cref="Quantity"/> servings.</summary>
    public decimal CarbohydrateG { get; set; }

    /// <summary>Total protein for <see cref="Quantity"/> servings.</summary>
    public decimal ProteinG { get; set; }

    /// <summary>Every other measured nutrient, also as totals.</summary>
    public List<LogItemNutrient> Nutrients { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; }
}
