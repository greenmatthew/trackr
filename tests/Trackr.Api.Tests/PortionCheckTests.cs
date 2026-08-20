using Trackr.Api.Cascade;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// What a meal comes to once its counts are applied - the gap milestone 9 shipped with.
/// </summary>
/// <remarks>
/// Every other check runs on one serving. A quantity that is really a gram weight passes all of
/// them, because each number is fine on its own and only the product is absurd.
/// </remarks>
public sealed class PortionCheckTests
{
    /// <summary>
    /// The reply from the run recorded in docs/decisions/11-chat.md, and the reason this check
    /// lives on the assembled item rather than in the reader.
    /// </summary>
    /// <remarks>
    /// This item is a full Open Food Facts match: every figure is the database's and the quantity
    /// is the only thing the model contributed, so the reader's verdict has already been dropped by
    /// the time it looks like this. Checking here is the only place the offending pair exists.
    /// </remarks>
    [Fact]
    public void A_quantity_that_is_really_a_gram_weight_is_flagged()
    {
        var checked_ = PortionCheck.Apply([
            Item(name: "Chocolate Therapy", quantity: 131m, energyKcal: 330m, servingSize: 131m)
        ]);

        var item = Assert.Single(checked_);

        Assert.Equal(AnalysisConfidence.Low, item.Confidence);
        Assert.Equal(131m, item.Quantity);
        Assert.Equal(330m, item.EnergyKcal);
        Assert.Contains(item.Warnings, warning => warning.Contains("43230 kcal", StringComparison.Ordinal));
    }

    /// <remarks>
    /// The half the energy ceiling cannot see: a hundred servings of lettuce is a trivial number of
    /// kilocalories and thirteen kilograms of lettuce.
    /// </remarks>
    [Fact]
    public void A_portion_weighing_more_than_anyone_could_eat_is_flagged()
    {
        var checked_ = PortionCheck.Apply([
            Item(name: "Lettuce", quantity: 131m, energyKcal: 15m, servingSize: 100m)
        ]);

        var item = Assert.Single(checked_);

        Assert.Equal(AnalysisConfidence.Low, item.Confidence);
        Assert.Contains(item.Warnings, warning => warning.Contains("13.1 kg", StringComparison.Ordinal));
    }

    /// <remarks>
    /// The ceiling is on a misread, not on a big appetite. A check that fires on a portion somebody
    /// could plausibly have eaten is one people learn to ignore.
    /// </remarks>
    [Fact]
    public void A_large_but_believable_portion_is_not_flagged()
    {
        var checked_ = PortionCheck.Apply([Item(quantity: 10m, energyKcal: 800m, servingSize: null)]);

        var item = Assert.Single(checked_);

        Assert.Equal(AnalysisConfidence.Normal, item.Confidence);
        Assert.Empty(item.Warnings);
    }

    /// <remarks>
    /// No line here is absurd on its own, but nobody ate them all in one sitting. Every item is
    /// flagged, because it is the sum that is wrong and nothing here can tell which line spoiled it.
    /// </remarks>
    [Fact]
    public void A_meal_that_adds_up_to_a_week_of_food_is_flagged()
    {
        var oil = Item(name: "Oil", quantity: 1m, energyKcal: 4_500m, servingSize: null);

        var checked_ = PortionCheck.Apply([oil, oil, oil, oil, oil]);

        Assert.Equal(5, checked_.Count);
        Assert.All(checked_, item => Assert.Equal(AnalysisConfidence.Low, item.Confidence));
        Assert.All(
            checked_,
            item => Assert.Contains(
                item.Warnings,
                warning => warning.Contains("week of food", StringComparison.Ordinal)));
    }

    [Fact]
    public void An_ordinary_several_course_meal_is_not_flagged()
    {
        var checked_ = PortionCheck.Apply([Item(), Item(), Item()]);

        Assert.All(checked_, item => Assert.Equal(AnalysisConfidence.Normal, item.Confidence));
    }

    /// <remarks>
    /// A verdict already reached upstream is not undone here: confidence is a one-way latch, and
    /// this check adds to it rather than replacing it.
    /// </remarks>
    [Fact]
    public void An_item_already_flagged_stays_flagged()
    {
        var checked_ = PortionCheck.Apply([
            Item() with { Confidence = AnalysisConfidence.Low, Warnings = ["Something else was wrong."] }
        ]);

        var item = Assert.Single(checked_);

        Assert.Equal(AnalysisConfidence.Low, item.Confidence);
        Assert.Contains("Something else was wrong.", item.Warnings);
    }

    private static MealAnalysisItem Item(
        string name = "Toast",
        decimal quantity = 1m,
        decimal energyKcal = 80m,
        decimal? servingSize = 40m) =>
        new(
            Name: name,
            Brand: null,
            Barcode: null,
            MealImageId: null,
            Source: AnalyzedItemSource.Model,
            Confidence: AnalysisConfidence.Normal,
            Quantity: quantity,
            ServingSize: servingSize,
            ServingUnit: servingSize is null ? null : "g",
            EnergyKcal: energyKcal,
            FatG: 1m,
            CarbohydrateG: 15m,
            ProteinG: 3m,
            Nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal),
            Warnings: []);
}
