using Microsoft.AspNetCore.Identity;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Identity;

/// <summary>
/// An account. Everything about credentials, 2FA and lockout comes from
/// <see cref="IdentityUser{TKey}"/> - CLAUDE.md section 10 is explicit that we do not
/// hand-roll any of it.
/// </summary>
/// <remarks>
/// The key is a <see cref="Guid"/> rather than Identity's default string because every
/// table from milestone 6 onwards (food catalog, log entries, nutrient snapshots) gets
/// an owner foreign key, and Postgres stores a uuid in 16 bytes against 37 for the text
/// form. This choice is effectively permanent once the first migration is applied.
/// </remarks>
public class TrackrUser : IdentityUser<Guid>
{
    public TrackrUser()
    {
        // The string-keyed IdentityUser assigns its own Id in the constructor, but the
        // generic IdentityUser<TKey> cannot - it has no way to make an arbitrary TKey.
        // Without this line a new user is saved with Guid.Empty, and the second one
        // fails on the primary key. Version 7 GUIDs are time-ordered, so the primary
        // key index keeps appending rather than fragmenting on random inserts.
        Id = Guid.CreateVersion7();
    }

    /// <summary>When the account was created. Identity itself does not track this.</summary>
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The IANA zone the account's days are measured in - "Europe/London". Null means UTC.
    /// </summary>
    /// <remarks>
    /// <strong>On the user rather than sent with each request, and CLAUDE.md section 9.13 says why:
    /// the server aggregates.</strong> The phone knows its own zone, which is the tempting
    /// shortcut, but a total is computed here and has to agree with itself between the chat, the
    /// stats views, the goals and whatever asks next. One of those clients being in an airport
    /// must not move somebody's midnight.
    /// <para>
    /// Nullable rather than defaulted to a string, so "never set" stays distinguishable from
    /// "deliberately UTC". <see cref="Time.DayBoundary"/> is the only thing that reads it.
    /// </para>
    /// </remarks>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// What to call the account holder, or null to fall back to the email address.
    /// </summary>
    /// <remarks>
    /// Not an identifier and not unique. The account is keyed by email and logs in with it; this is
    /// only what a greeting and a set of initials read from, and two people in one household may
    /// perfectly well share a name.
    /// </remarks>
    public string? DisplayName { get; set; }

    /// <summary>Which unit to <em>show</em> energy in. Nothing stored ever changes unit.</summary>
    /// <remarks>
    /// The whole of it is on the way to the screen. Totals, targets and the snapshots on log items
    /// are kilocalories in Postgres and kilocalories on the wire, because a stored number that
    /// changed meaning when a setting changed would rewrite history - see
    /// <see cref="Shared.Nutrition.EnergyUnit"/>.
    /// </remarks>
    public EnergyUnit EnergyUnit { get; set; } = EnergyUnit.Kilocalories;

    /// <summary>Which units body measurements are shown and typed in. Deliberately not nutrition.</summary>
    public MeasurementSystem MeasurementSystem { get; set; } = MeasurementSystem.Metric;

    /// <summary>
    /// The optional body measurements, kept only so a calorie target can be suggested.
    /// </summary>
    /// <remarks>
    /// <strong>Columns on the account rather than a table of their own, and that is a decision with
    /// an expiry date.</strong> One current weight is one value; a weight <em>history</em> is a
    /// series, and the day somebody wants a chart of it, it becomes its own entity rather than a
    /// wider user row. Section 9.14.
    /// <para>
    /// Always metric. <see cref="MeasurementSystem"/> decides how they are typed and drawn, and the
    /// client converts - two units for one column is how a weight becomes ambiguous.
    /// </para>
    /// </remarks>
    public BiologicalSex Sex { get; set; } = BiologicalSex.Unspecified;

    /// <summary>The date rather than the age, which would be wrong within a year of being stored.</summary>
    public DateOnly? BirthDate { get; set; }

    public decimal? HeightCm { get; set; }

    public decimal? WeightKg { get; set; }

    public ActivityLevel ActivityLevel { get; set; } = ActivityLevel.Sedentary;

    /// <summary>
    /// When the profile picture last changed, or null if there is none.
    /// </summary>
    /// <remarks>
    /// On the user row rather than only on <see cref="Data.UserAvatar"/> so that "is there an
    /// avatar, and is the client's copy stale" is answered by the row every request already
    /// loads, instead of a join that would carry the image bytes along with it. The avatar
    /// endpoints keep the two in step.
    /// </remarks>
    public DateTimeOffset? AvatarUpdatedUtc { get; set; }
}
