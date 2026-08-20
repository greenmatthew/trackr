using System.Globalization;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Checks what a meal actually comes to, once every count is applied.
/// </summary>
/// <remarks>
/// Every other check in this folder is about one serving, and a quantity that is really a gram
/// weight slips past all of them: 330 kcal is a reasonable serving and 131 is a reasonable count,
/// and only their product is absurd. That is the reply milestone 9 shipped with - a 43 230 kcal
/// meal with nothing marked wrong.
/// <para>
/// <strong>It runs on the assembled item rather than inside <see cref="MealAnalysisReader"/>, and
/// that placement is the whole point.</strong> On a full barcode match the quantity is the only
/// number the model contributes: everything else is replaced by the database's, and
/// <c>MealCascade.FromProduct</c> drops the reader's warnings for exactly that reason. So the pair
/// that has to be judged - the model's count against the database's figures - is a pair the reader
/// never sees. Checking here is the only place it exists.
/// </para>
/// <para>
/// Everything below flags and nothing clamps. The count is the most likely thing to be wrong, it
/// is the one figure on the card that is editable, and nothing here can tell what it should have
/// been.
/// </para>
/// </remarks>
public static class PortionCheck
{
    /// <summary>
    /// A ceiling on what one item comes to. Several days of food on one line is a misread.
    /// </summary>
    private const decimal ImplausiblePortionEnergyKcal = 10_000m;

    /// <summary>
    /// The same for the meal, where no one item is absurd but the sum is.
    /// </summary>
    private const decimal ImplausibleMealEnergyKcal = 20_000m;

    /// <summary>
    /// A ceiling on what one item weighs, in grams.
    /// </summary>
    /// <remarks>
    /// The energy ceiling misses the low-calorie half of the same misread: a hundred-odd servings
    /// of lettuce is a trivial number of kilocalories and thirteen kilograms of lettuce.
    /// </remarks>
    private const decimal ImplausiblePortionGrams = 5_000m;

    public static IReadOnlyList<MealAnalysisItem> Apply(IReadOnlyList<MealAnalysisItem> items)
    {
        var checked_ = items.Select(CheckItem).ToList();

        return FlagImplausibleMeal(checked_);
    }

    private static MealAnalysisItem CheckItem(MealAnalysisItem item)
    {
        var warnings = new List<string>();

        var total = Portion(item.EnergyKcal, item.Quantity);

        if (total > ImplausiblePortionEnergyKcal)
        {
            var described = total is { } value
                ? string.Create(CultureInfo.InvariantCulture, $"{value:0} kcal")
                : "more kilocalories than this server can count";

            warnings.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{item.Quantity:0.###} x {item.Name} comes to {described}, which is several days of food. The amount is the most likely thing to be wrong."));
        }

        if (item.ServingSize is { } size && IsMassUnit(item.ServingUnit))
        {
            var weight = Portion(size, item.Quantity);

            if (weight > ImplausiblePortionGrams)
            {
                var weighed = weight is { } grams
                    ? string.Create(CultureInfo.InvariantCulture, $"{grams / 1000m:0.#} kg")
                    : "more than this server can weigh";

                warnings.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{item.Quantity:0.###} x {item.Name} comes to {weighed} of food. The amount is the most likely thing to be wrong."));
            }
        }

        return warnings.Count == 0 ? item : Flag(item, warnings);
    }

    /// <summary>
    /// Flags every item when the reply as a whole adds up to something nobody ate.
    /// </summary>
    /// <remarks>
    /// A per-item ceiling misses the meal assembled from ten individually plausible lines, and the
    /// mistake behind one is the mistake behind the other. Every item is flagged rather than a
    /// chosen one, because it is the sum that is wrong and nothing here can tell which line spoiled
    /// it.
    /// </remarks>
    private static IReadOnlyList<MealAnalysisItem> FlagImplausibleMeal(List<MealAnalysisItem> items)
    {
        var total = 0m;

        foreach (var item in items)
        {
            // Saturated per item, so one absurd line cannot overflow the running sum - and so an
            // item already flagged on its own does not drag the whole meal in behind it.
            total += Math.Min(
                Portion(item.EnergyKcal, item.Quantity) ?? ImplausibleMealEnergyKcal,
                ImplausibleMealEnergyKcal);

            if (total > ImplausibleMealEnergyKcal)
            {
                break;
            }
        }

        if (total <= ImplausibleMealEnergyKcal)
        {
            return items;
        }

        var warning = string.Create(
            CultureInfo.InvariantCulture,
            $"These come to more than {ImplausibleMealEnergyKcal:0} kcal between them, which is a week of food rather than a meal. Please check the amounts.");

        return [.. items.Select(item => Flag(item, [warning]))];
    }

    private static MealAnalysisItem Flag(MealAnalysisItem item, IReadOnlyList<string> warnings) =>
        item with
        {
            Confidence = AnalysisConfidence.Low,
            Warnings = [.. item.Warnings, .. warnings]
        };

    /// <summary>
    /// One item's total, or null when the two numbers are too large to multiply.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception because these can originate as unconstrained JSON numbers: a
    /// decimal holds 28 digits, a reply may carry more, and a check that throws on the worst input
    /// it will ever see is worse than no check at all. Null is treated as over every ceiling, which
    /// is what it is.
    /// </remarks>
    private static decimal? Portion(decimal perServing, decimal quantity)
    {
        try
        {
            return perServing * quantity;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>A serving measured in something a weight can be compared against.</summary>
    public static bool IsMassUnit(string? unit) =>
        unit is not null
        && (unit.Equals("g", StringComparison.OrdinalIgnoreCase)
            || unit.Equals("ml", StringComparison.OrdinalIgnoreCase));
}
