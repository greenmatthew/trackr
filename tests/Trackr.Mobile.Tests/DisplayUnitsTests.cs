using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Mobile.Tests;

/// <summary>
/// The account's unit preferences, applied on the way to the screen and back.
/// </summary>
/// <remarks>
/// The rule these all serve: <strong>the API stores and reports kilocalories</strong>, and a unit
/// preference changes how a figure is drawn and typed, never what is stored. So the interesting
/// property is not the arithmetic - it is that a number nobody edited survives the round trip
/// unchanged.
/// </remarks>
public sealed class DisplayUnitsTests
{
    [Fact]
    public void Kilocalories_are_left_exactly_as_the_server_reported_them()
    {
        var units = Units.For(EnergyUnit.Kilocalories);

        Assert.Equal("kcal", units.EnergySymbol);
        Assert.Equal(1_850m, units.EnergyFromStored(1_850m));
        Assert.Equal("1850 kcal", units.FormatEnergy(1_850m));
    }

    [Fact]
    public void Kilojoules_use_the_defined_conversion_rather_than_the_label_rounding()
    {
        var units = Units.For(EnergyUnit.Kilojoules);

        Assert.Equal("kJ", units.EnergySymbol);

        // 4.184 exactly, which is the definition. EU labelling rounds it to 4.2 in places; using
        // the defined value is what makes the round trip below exact.
        Assert.Equal(8_368m, units.EnergyFromStored(2_000m));
    }

    /// <summary>
    /// A figure shown and saved untouched is the figure that was stored.
    /// </summary>
    /// <remarks>
    /// The whole reason nothing is rounded on the way out. A preference that silently moved
    /// numbers on every visit to a screen would be the "silent wrong number" CLAUDE.md section 2
    /// puts first, arriving by a route nobody would think to look at.
    /// </remarks>
    [Theory]
    [InlineData(2_000)]
    [InlineData(1_850)]
    [InlineData(78)]
    [InlineData(0.5)]
    public void A_figure_nobody_edited_comes_back_as_itself(decimal kilocalories)
    {
        var units = Units.For(EnergyUnit.Kilojoules);

        Assert.Equal(kilocalories, units.EnergyToStored(units.EnergyFromStored(kilocalories)));
    }

    /// <remarks>
    /// Nutrition labels are metric the world over, so ounces of protein would be a number that is
    /// on no package the user can pick up - see <see cref="MeasurementSystem"/>.
    /// </remarks>
    [Fact]
    public void Imperial_does_not_turn_grams_into_ounces()
    {
        var units = Units.For(EnergyUnit.Kilocalories, MeasurementSystem.Imperial);

        Assert.Equal("g", units.SymbolFor(NutrientUnit.Gram));
        Assert.Equal(12m, units.FromStored(NutrientUnit.Gram, 12m));
        Assert.Equal("mg", units.SymbolFor(NutrientUnit.Milligram));
    }

    /// <remarks>
    /// Only energy has a preference at all, so the nutrient's own unit answers for everything else
    /// and the energy one is the account's.
    /// </remarks>
    [Fact]
    public void Only_the_energy_nutrient_follows_the_preference()
    {
        var units = Units.For(EnergyUnit.Kilojoules);

        Assert.Equal("kJ", units.SymbolFor(NutrientUnit.Kilocalorie));
        Assert.Equal(418.4m, units.FromStored(NutrientUnit.Kilocalorie, 100m));
        Assert.Equal(100m, units.FromStored(NutrientUnit.Microgram, 100m));
    }

    /// <remarks>
    /// A signed-out screen has no account to read a preference from, and must draw what the API
    /// itself uses rather than nothing at all.
    /// </remarks>
    [Fact]
    public void With_no_account_the_api_s_own_units_are_used()
    {
        var units = new Trackr.Mobile.Core.Nutrition.DisplayUnits(new NoAccount());

        Assert.Equal(EnergyUnit.Kilocalories, units.Energy);
        Assert.False(units.IsImperial);
    }

    private sealed class NoAccount : Trackr.Mobile.Core.Auth.ICurrentAccount
    {
        public Trackr.Shared.Auth.MeResponse? CurrentUser => null;
    }
}
