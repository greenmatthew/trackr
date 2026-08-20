using System.Text.Json.Serialization;

namespace Trackr.Shared.Nutrition;

/// <summary>Which unit energy is shown in.</summary>
/// <remarks>
/// <strong>A display preference, never a storage one.</strong> Everything stored, summed, targeted
/// and reported by the API is kilocalories, for the same reason every nutrient amount is kept in
/// the unit its catalog row names: one vocabulary, one answer. A snapshot on a log item is a
/// historical record and must not change meaning because somebody changed a setting years later.
/// <para>
/// So this travels with the account, the client converts on the way to the screen and back on the
/// way in, and no value in Postgres ever moves.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<EnergyUnit>))]
public enum EnergyUnit
{
    /// <summary>What the API stores and what most food labels lead with.</summary>
    Kilocalories,

    /// <summary>The SI unit, and the one European labels put first.</summary>
    Kilojoules
}

/// <summary>Which units body measurements are entered and shown in.</summary>
/// <remarks>
/// <strong>Body measurements, and deliberately not nutrition.</strong> Choosing imperial does not
/// turn grams of protein into ounces: nutrition labels are metric the world over, so converting
/// them would put a number on screen that appears on no package the user can pick up. Height and
/// weight are the measurements people genuinely hold in feet and pounds, and they are the ones this
/// governs.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<MeasurementSystem>))]
public enum MeasurementSystem
{
    Metric,

    Imperial
}

/// <summary>
/// Turning stored units into shown ones, and back.
/// </summary>
/// <remarks>
/// In <c>Trackr.Shared</c> so that the phone and the web app cannot disagree about what 2 000 kcal
/// is in kilojoules. Pure arithmetic with no dependencies, which is what this project keeps in here
/// (CLAUDE.md section 3 - it is trimmed into the browser bundle and linked into the APK).
/// <para>
/// Every method rounds nothing. Rounding is a formatting decision and belongs where the number is
/// formatted; rounding here would compound on the way back in, so that entering 2 000 kcal as
/// 8 368 kJ and reading it again would slowly drift.
/// </para>
/// </remarks>
public static class UnitConversion
{
    /// <summary>The thermochemical calorie, which is what nutrition labelling uses.</summary>
    /// <remarks>
    /// Exactly 4.184 by definition rather than measured, so this is a conversion and not an
    /// approximation. EU labelling rounds it to 4.2 in places; using the defined value keeps a
    /// round trip through this class exact.
    /// </remarks>
    public const decimal KilojoulesPerKilocalorie = 4.184m;

    public const decimal CentimetresPerInch = 2.54m;

    public const decimal InchesPerFoot = 12m;

    public const decimal KilogramsPerPound = 0.45359237m;

    public static decimal ToKilojoules(decimal kilocalories) =>
        kilocalories * KilojoulesPerKilocalorie;

    public static decimal ToKilocalories(decimal kilojoules) =>
        kilojoules / KilojoulesPerKilocalorie;

    /// <summary>The stored kilocalorie figure, in whichever unit is being shown.</summary>
    public static decimal EnergyFromStored(decimal kilocalories, EnergyUnit unit) =>
        unit is EnergyUnit.Kilojoules ? ToKilojoules(kilocalories) : kilocalories;

    /// <summary>A figure a person typed, back into the kilocalories the API stores.</summary>
    public static decimal EnergyToStored(decimal shown, EnergyUnit unit) =>
        unit is EnergyUnit.Kilojoules ? ToKilocalories(shown) : shown;

    public static decimal CentimetresToInches(decimal centimetres) => centimetres / CentimetresPerInch;

    public static decimal InchesToCentimetres(decimal inches) => inches * CentimetresPerInch;

    public static decimal KilogramsToPounds(decimal kilograms) => kilograms / KilogramsPerPound;

    public static decimal PoundsToKilograms(decimal pounds) => pounds * KilogramsPerPound;

    /// <summary>A height in centimetres as whole feet plus the inches left over.</summary>
    /// <remarks>
    /// The inches are not rounded here. A caller that rounds them to the nearest whole inch must
    /// handle twelve, which is the classic way "5 ft 12 in" reaches a screen.
    /// </remarks>
    public static (int Feet, decimal Inches) CentimetresToFeetAndInches(decimal centimetres)
    {
        var total = CentimetresToInches(centimetres);
        var feet = (int)decimal.Floor(total / InchesPerFoot);

        return (feet, total - (feet * InchesPerFoot));
    }

    public static decimal FeetAndInchesToCentimetres(int feet, decimal inches) =>
        InchesToCentimetres((feet * InchesPerFoot) + inches);
}
