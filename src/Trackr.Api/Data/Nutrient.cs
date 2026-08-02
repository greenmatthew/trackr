using Trackr.Shared.Nutrition;

namespace Trackr.Api.Data;

/// <summary>
/// One nutrient the server knows how to record: its key, its display name and - above all - the
/// unit its amounts are stored in.
/// </summary>
/// <remarks>
/// The reference half of CLAUDE.md section 7's extensible nutrient store. Adding selenium is a row
/// here, not a migration, which is the property wiki/Nutrient-Reference.md promises.
/// <para>
/// The primary key is the human-chosen <see cref="Key"/> rather than an int surrogate. That makes
/// the foreign key on an amount row already equal to the key the wire format uses, so projecting
/// an item's nutrient map needs no join to this table at all - and that projection is the hot read
/// path for every catalog GET, every log GET and every milestone-11 aggregate. Seeding also needs
/// stable primary keys, and a key chosen by a person is stable by construction. The cost is about
/// twelve bytes per amount row instead of two.
/// </para>
/// <para>
/// Rows are inserted and updated by <see cref="NutrientSeed"/> at startup and are never deleted:
/// the amount tables reference this one with <c>Restrict</c>, so deleting a nutrient somebody has
/// measured must fail loudly. Retiring a nutrient later is a flag on the row, never a DELETE.
/// </para>
/// </remarks>
public class Nutrient
{
    /// <summary>The stable identifier, e.g. <c>vitamin_c</c>. Also the key used on the wire.</summary>
    public string Key { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The unit every amount of this nutrient is stored in.
    /// </summary>
    /// <remarks>
    /// Here rather than on the amount row, deliberately. Two sodium measurements recorded in
    /// different units would make a <c>SUM</c> silently wrong, and a silently wrong nutrition
    /// figure is the failure this whole design is arranged to avoid.
    /// </remarks>
    public NutrientUnit Unit { get; set; }

    /// <summary>Which section of a nutrition label this belongs to.</summary>
    public NutrientGroup Group { get; set; }

    /// <summary>
    /// Label order, spaced by 10 so inserting a nutrient never renumbers the others.
    /// </summary>
    /// <remarks>
    /// Independent of <see cref="Group"/>: a label interleaves the core four with their
    /// breakdowns, so groups are not contiguous in this ordering. That is intended.
    /// </remarks>
    public int SortOrder { get; set; }

    /// <summary>
    /// True for energy, fat, carbohydrate and protein.
    /// </summary>
    /// <remarks>
    /// Those four are typed columns on <see cref="FoodItem"/> and <see cref="LogItem"/> and are
    /// deliberately never rows in the amount tables - a database CHECK enforces it. They still
    /// appear here so that key, display name, unit, group and sort order for every nutrient live
    /// in exactly one place, and so <c>GET /api/nutrients</c> returns a complete ordered catalog a
    /// client can render uniformly.
    /// </remarks>
    public bool IsCore { get; set; }
}
