using Trackr.Shared.Auth;

namespace Trackr.Api.Identity;

/// <summary>
/// The account, as its own clients see it.
/// </summary>
/// <remarks>
/// One place, because there were three by the time the profile arrived - registration, sign-in and
/// the time-zone write each built a <see cref="MeResponse"/> by hand, and each new field on the
/// account was a chance for one of them to be forgotten. A field missing from one construction is
/// a client that renders correctly everywhere except immediately after registering, which is
/// exactly the kind of bug nobody reproduces.
/// </remarks>
public static class AccountMapping
{
    public static MeResponse ToMeResponse(this TrackrUser user) =>
        new(
            user.Id,
            user.Email!,
            user.TwoFactorEnabled,
            user.AvatarUpdatedUtc,
            user.TimeZoneId,
            user.DisplayName,
            user.EnergyUnit,
            user.MeasurementSystem);

    /// <param name="today">
    /// The account's local today, used only to turn a birth date into an age. The account's own
    /// day, so a birthday turns over on the same midnight every total in Trackr uses.
    /// </param>
    public static ProfileResponse ToProfileResponse(this TrackrUser user, DateOnly today) =>
        new(
            user.DisplayName,
            user.TimeZoneId,
            user.EnergyUnit,
            user.MeasurementSystem,
            new BodyMetricsResponse(
                user.Sex,
                user.BirthDate,
                user.HeightCm,
                user.WeightKg,
                user.ActivityLevel),
            EnergyEstimate.For(
                user.Sex,
                user.BirthDate,
                user.HeightCm,
                user.WeightKg,
                user.ActivityLevel,
                today));
}
