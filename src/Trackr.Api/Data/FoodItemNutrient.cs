namespace Trackr.Api.Data;

/// <summary>
/// How much of one nutrient is in one serving of one catalog item.
/// </summary>
/// <remarks>
/// The amount half of the extensible nutrient store. The primary key is the pair
/// (<see cref="FoodItemId"/>, <see cref="NutrientKey"/>) with no surrogate: that pair <em>is</em>
/// the identity, it makes "one row per nutrient per item" a constraint rather than a convention,
/// and it clusters an item's rows together - which is the only way they are ever read.
/// <para>
/// A nutrient with no row here is <strong>not measured</strong>; a row holding zero is
/// <strong>known to be zero</strong>. Those are different facts and the API keeps them apart all
/// the way to the wire, where absence from the JSON map means the former.
/// </para>
/// <para>
/// The four core nutrients are excluded by a database CHECK constraint, because they live in
/// columns on <see cref="FoodItem"/>. Storing them here as well would double count for any client
/// that summed the map and then added the typed fields.
/// </para>
/// </remarks>
public class FoodItemNutrient
{
    public Guid FoodItemId { get; set; }

    public FoodItem? FoodItem { get; set; }

    /// <summary>References <see cref="Nutrient.Key"/>, and is also the key used on the wire.</summary>
    public string NutrientKey { get; set; } = "";

    public Nutrient? Nutrient { get; set; }

    /// <summary>
    /// The amount in one serving, in <see cref="Nutrient.Unit"/> - never in a unit of its own.
    /// </summary>
    /// <remarks>
    /// <c>decimal</c> mapped to <c>numeric(12,4)</c> rather than <c>double</c>: milestone 11 sums
    /// thousands of label values, and binary floating point accumulates error that exact decimal
    /// arithmetic does not.
    /// </remarks>
    public decimal Amount { get; set; }
}
