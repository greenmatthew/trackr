using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Trackr.Shared.Nutrition;

namespace Trackr.Shared.Auth;

/// <summary>
/// Which of the two Mifflin-St Jeor constants applies.
/// </summary>
/// <remarks>
/// <strong>Named for what the equation actually needs, which is not the same as how anybody
/// identifies.</strong> Every published BMR formula has two constants and no third, so this is a
/// coefficient rather than a profile field, and <see cref="Unspecified"/> is a first-class answer:
/// it costs an estimate, not the account. Nothing else in Trackr reads it, and a hand-typed target
/// works regardless (CLAUDE.md section 9.12).
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BiologicalSex>))]
public enum BiologicalSex
{
    /// <summary>The default, and the reason an estimate is optional rather than assumed.</summary>
    Unspecified,

    Female,

    Male
}

/// <summary>How much of the day is spent moving, as a multiplier on resting energy.</summary>
/// <remarks>
/// The five steps every TDEE calculator uses, because they are the ones people can place
/// themselves in. The multipliers live on the server - see <c>EnergyEstimate</c>.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ActivityLevel>))]
public enum ActivityLevel
{
    /// <summary>Desk work and little else.</summary>
    Sedentary,

    /// <summary>Light exercise one to three days a week.</summary>
    Light,

    /// <summary>Moderate exercise three to five days a week.</summary>
    Moderate,

    /// <summary>Hard exercise six or seven days a week.</summary>
    Active,

    /// <summary>Hard exercise daily, or a physical job on top of it.</summary>
    VeryActive
}

/// <summary>
/// The optional body measurements, kept so goals can suggest a number instead of asking the user
/// to invent one.
/// </summary>
/// <remarks>
/// <strong>Always metric on the wire.</strong> <see cref="MeasurementSystem"/> decides how they are
/// shown and typed; the client converts. Two units for one number in the database is how a weight
/// ends up ambiguous.
/// <para>
/// Every measurement is nullable and null is the ordinary state. This is the most personal data in
/// the application and nothing outside the estimate reads it, so a profile with none of it filled
/// in is a complete profile.
/// </para>
/// </remarks>
/// <param name="BirthDate">
/// The date, not the age, because an age stored today is wrong within a year.
/// </param>
public sealed record BodyMetricsResponse(
    BiologicalSex Sex,
    DateOnly? BirthDate,
    decimal? HeightCm,
    decimal? WeightKg,
    ActivityLevel ActivityLevel)
{
    /// <summary>Nothing recorded - what a new account has.</summary>
    public static BodyMetricsResponse Empty { get; } =
        new(BiologicalSex.Unspecified, null, null, null, ActivityLevel.Sedentary);

    /// <summary>Whether anything at all has been filled in.</summary>
    public bool HasAnything =>
        Sex is not BiologicalSex.Unspecified
        || BirthDate is not null
        || HeightCm is not null
        || WeightKg is not null;
}

/// <summary>
/// What the body measurements imply about a day's energy, in kilocalories.
/// </summary>
/// <remarks>
/// Null on the profile whenever a measurement the equation needs is missing - which is most
/// accounts, and is not an error. A refusal to guess: an estimate built on a height nobody entered
/// would be a made-up number wearing the authority of a calculation.
/// </remarks>
/// <param name="BasalKcal">Resting energy - what the body spends doing nothing at all.</param>
/// <param name="MaintenanceKcal">
/// Basal times the activity multiplier: roughly what holds weight steady. This is the figure a
/// calorie target is built from.
/// </param>
public sealed record EnergyEstimateResponse(decimal BasalKcal, decimal MaintenanceKcal);

/// <summary>
/// The whole of an account's profile: what to call them, how to show numbers, and the optional
/// measurements behind an energy estimate.
/// </summary>
/// <remarks>
/// One read, three writes. The writes are split by concern rather than merged into a patch, so
/// that <em>clearing</em> a measurement stays expressible: a request where every field is optional
/// cannot tell "leave my weight alone" from "I no longer want a weight stored", and body
/// measurements are exactly the thing somebody may want gone.
/// </remarks>
/// <param name="TimeZoneId">
/// Reported here for completeness, but written by <c>PUT /api/account/timezone</c>, which validates
/// it against this server's own zone database.
/// </param>
public sealed record ProfileResponse(
    string? DisplayName,
    string? TimeZoneId,
    EnergyUnit EnergyUnit,
    MeasurementSystem MeasurementSystem,
    BodyMetricsResponse Body,
    EnergyEstimateResponse? Energy);

/// <summary>
/// The bounds a body measurement has to be inside.
/// </summary>
/// <remarks>
/// Constants rather than numbers written twice, because the client checks them to give an
/// immediate answer and the server checks them because it is the server. Two copies of "500" is
/// how a client starts refusing something the API would have accepted.
/// <para>
/// Wide on purpose. These catch a slipped decimal point or a height typed in millimetres, not an
/// unusual body - a bound tight enough to be interesting would eventually tell somebody real that
/// they do not exist.
/// </para>
/// </remarks>
public static class BodyMetricLimits
{
    public const double MinHeightCm = 50d;

    public const double MaxHeightCm = 260d;

    public const double MinWeightKg = 20d;

    public const double MaxWeightKg = 500d;

    /// <summary>Beyond this a birth date is a typed year, not an age.</summary>
    public const int MaxAgeYears = 120;
}

/// <summary>What to call the account and how it wants numbers shown.</summary>
public sealed class SaveProfileRequest
{
    /// <summary>
    /// A name for greetings and initials, or null to go back to using the email address.
    /// </summary>
    /// <remarks>
    /// Not a second identifier: the account is still keyed by email, still logs in with it, and two
    /// household members may quite reasonably both be called Sam.
    /// </remarks>
    [StringLength(60)]
    public string? DisplayName { get; set; }

    public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.Kilocalories;

    public MeasurementSystem MeasurementSystem { get; set; } = MeasurementSystem.Metric;
}

/// <summary>
/// The body measurements, replaced wholesale.
/// </summary>
/// <remarks>
/// Wholesale so that clearing one is a request rather than a missing feature. Sending null for
/// every measurement is how an account removes them, and it is expected to work.
/// </remarks>
public sealed class SaveBodyMetricsRequest
{
    public BiologicalSex Sex { get; set; } = BiologicalSex.Unspecified;

    public DateOnly? BirthDate { get; set; }

    /// <summary>Centimetres, always. The client converts if it shows feet and inches.</summary>
    [Range(BodyMetricLimits.MinHeightCm, BodyMetricLimits.MaxHeightCm)]
    public decimal? HeightCm { get; set; }

    /// <summary>Kilograms, always.</summary>
    [Range(BodyMetricLimits.MinWeightKg, BodyMetricLimits.MaxWeightKg)]
    public decimal? WeightKg { get; set; }

    public ActivityLevel ActivityLevel { get; set; } = ActivityLevel.Sedentary;
}
