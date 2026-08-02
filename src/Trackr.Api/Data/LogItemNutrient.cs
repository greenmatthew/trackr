namespace Trackr.Api.Data;

/// <summary>
/// How much of one nutrient a logged item contained, in total, for the quantity eaten.
/// </summary>
/// <remarks>
/// The same shape and the same rules as <see cref="FoodItemNutrient"/> - composite key, cascade
/// from its parent, restrict to <see cref="Nutrient"/>, no core nutrients - with one difference
/// that matters: <see cref="Amount"/> is a total rather than a per-serving figure. See
/// <see cref="LogItem"/> for why.
/// <para>
/// A separate table rather than a shared one with a discriminator: the two have different parents,
/// different lifetimes and different meanings, and the only thing they share is a column list.
/// </para>
/// </remarks>
public class LogItemNutrient
{
    public Guid LogItemId { get; set; }

    public LogItem? LogItem { get; set; }

    /// <summary>References <see cref="Nutrient.Key"/>, and is also the key used on the wire.</summary>
    public string NutrientKey { get; set; } = "";

    public Nutrient? Nutrient { get; set; }

    /// <summary>The total for the logged quantity, in <see cref="Nutrient.Unit"/>.</summary>
    public decimal Amount { get; set; }
}
