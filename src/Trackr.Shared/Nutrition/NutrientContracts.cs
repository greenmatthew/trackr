using System.Text.Json.Serialization;

namespace Trackr.Shared.Nutrition;

/// <summary>
/// The units a nutrient amount can be recorded in.
/// </summary>
/// <remarks>
/// A closed enum rather than free text, deliberately. Adding a <em>nutrient</em> must be a data
/// change (wiki/Nutrient-Reference.md), but adding a <em>unit</em> cannot be: a unit code is
/// meaningless without a conversion factor, and that factor lives in code.
/// <para>
/// The unit belongs to the nutrient, never to an individual amount. Two sodium rows recorded in
/// different units would make a <c>SUM</c> silently wrong, which the same page names as worse
/// than not recording the value at all.
/// </para>
/// <para>
/// Energy is stored in kilocalories only. "kJ optional" is a display conversion (x4.184) for
/// milestone 13's unit preferences, not a second stored unit.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<NutrientUnit>))]
public enum NutrientUnit
{
    [JsonStringEnumMemberName("g")]
    Gram,

    [JsonStringEnumMemberName("mg")]
    Milligram,

    [JsonStringEnumMemberName("µg")]
    Microgram,

    [JsonStringEnumMemberName("kcal")]
    Kilocalorie
}

/// <summary>
/// How nutrients are grouped for display. Matches the section headings in
/// wiki/Nutrient-Reference.md.
/// </summary>
/// <remarks>
/// Independent of sort order: the label interleaves the core four with their breakdowns, so the
/// groups are deliberately not contiguous in <see cref="NutrientResponse.SortOrder"/>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<NutrientGroup>))]
public enum NutrientGroup
{
    Core,
    FatBreakdown,
    CarbohydrateBreakdown,
    SterolsAndElectrolytes,
    Vitamins,
    Minerals
}

/// <summary>
/// The four nutrients that are always present, and are therefore typed properties rather than
/// entries in a nutrient map.
/// </summary>
/// <remarks>
/// Named here rather than written out at each use because three places have to agree exactly: the
/// database CHECK constraint that keeps them out of the amount tables, the API validation that
/// rejects them in a request map, and any client merging the typed properties back into the map
/// for display.
/// </remarks>
public static class CoreNutrients
{
    public const string EnergyKcal = "energy_kcal";
    public const string Fat = "fat";
    public const string Carbohydrate = "carbohydrate";
    public const string Protein = "protein";

    public static readonly string[] Keys = [EnergyKcal, Fat, Carbohydrate, Protein];

    public static bool IsCore(string key) => Keys.Contains(key, StringComparer.Ordinal);
}

/// <summary>One nutrient the server knows about.</summary>
/// <param name="Key">The stable identifier, and the key used in every nutrient map on the wire.</param>
/// <param name="DisplayName">What to show a person.</param>
/// <param name="Unit">The unit every amount for this nutrient is recorded in.</param>
/// <param name="Group">Which section of a label it belongs to.</param>
/// <param name="SortOrder">Label order. Spaced by 10 so a new nutrient never renumbers the rest.</param>
/// <param name="IsCore">
/// True for the four always-present nutrients. These are typed properties on food items and log
/// items rather than entries in the nutrient map - a client that summed the map and then added
/// the typed fields would double count.
/// </param>
public sealed record NutrientResponse(
    string Key,
    string DisplayName,
    NutrientUnit Unit,
    NutrientGroup Group,
    int SortOrder,
    bool IsCore);
