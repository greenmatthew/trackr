using Trackr.Mobile.Core.Auth;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.Nutrition;

/// <summary>
/// The account's unit preferences, applied on the way to the screen and back.
/// </summary>
/// <remarks>
/// <strong>The server reports kilocalories and grams, always.</strong> A unit preference changes how
/// a number is drawn and typed, never what is stored or sent - the API's own contracts say so
/// (<see cref="EnergyUnit"/>), and a snapshot on a log item exists precisely so that history does
/// not move when a setting does. This class is the whole of the conversion, on this client.
/// <para>
/// Read from <see cref="ICurrentAccount"/> on every call rather than captured, so changing the
/// preference on the profile screen takes effect the next time any other screen redraws, with
/// nothing to invalidate.
/// </para>
/// <para>
/// <strong>Nothing is rounded on the way through.</strong> That is what makes a round trip exact:
/// 2 000 kcal shown as 8 368 kJ and saved untouched is 2 000 kcal again. Rounding here would move
/// numbers nobody edited, which is the failure mode CLAUDE.md section 2 names first.
/// </para>
/// </remarks>
public sealed class DisplayUnits(ICurrentAccount account)
{
    /// <summary>Falls back to the API's own units, which is what a signed-out screen should use.</summary>
    public EnergyUnit Energy => account.CurrentUser?.EnergyUnit ?? EnergyUnit.Kilocalories;

    public MeasurementSystem Measurements =>
        account.CurrentUser?.MeasurementSystem ?? MeasurementSystem.Metric;

    public bool IsImperial => Measurements is MeasurementSystem.Imperial;

    public string EnergySymbol => Energy is EnergyUnit.Kilojoules ? "kJ" : "kcal";

    /// <summary>A stored kilocalorie figure, in whatever unit the account reads in.</summary>
    public decimal EnergyFromStored(decimal kilocalories) =>
        UnitConversion.EnergyFromStored(kilocalories, Energy);

    /// <summary>
    /// A figure the user typed, back into the kilocalories the API stores.
    /// </summary>
    /// <remarks>
    /// Rounded to the four decimal places the server's own columns keep, and only there. Dividing
    /// by 4.184 can leave a trailing digit that makes an untouched 2 000 kcal come back as
    /// 1999.9999999, and rounding to what would be stored anyway cannot introduce a difference the
    /// server would not have made itself.
    /// </remarks>
    public decimal EnergyToStored(decimal shown) =>
        decimal.Round(UnitConversion.EnergyToStored(shown, Energy), 4, MidpointRounding.AwayFromZero);

    /// <summary>"1850 kcal", or "7740.4 kJ".</summary>
    public string FormatEnergy(decimal kilocalories) =>
        $"{NutrientRows.Format(EnergyFromStored(kilocalories))} {EnergySymbol}";

    /// <summary>The same, without the unit, for a screen that labels it separately.</summary>
    public string FormatEnergyAmount(decimal kilocalories) =>
        NutrientRows.Format(EnergyFromStored(kilocalories));

    /// <summary>
    /// The symbol for any nutrient's unit, with energy answered by the account's preference.
    /// </summary>
    /// <remarks>
    /// Only energy has a preference. Grams of protein stay grams whatever else is chosen: nutrition
    /// labels are metric worldwide, so ounces here would be a number that appears on no package -
    /// see <see cref="MeasurementSystem"/>.
    /// </remarks>
    public string SymbolFor(NutrientUnit unit) =>
        unit is NutrientUnit.Kilocalorie ? EnergySymbol : NutrientRows.UnitSymbol(unit);

    /// <summary>A stored amount of any nutrient, in the unit it will be shown in.</summary>
    public decimal FromStored(NutrientUnit unit, decimal amount) =>
        unit is NutrientUnit.Kilocalorie ? EnergyFromStored(amount) : amount;

    /// <summary>A shown amount of any nutrient, back into the unit the API stores.</summary>
    public decimal ToStored(NutrientUnit unit, decimal amount) =>
        unit is NutrientUnit.Kilocalorie ? EnergyToStored(amount) : amount;
}
