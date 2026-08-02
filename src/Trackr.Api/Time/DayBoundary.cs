using Trackr.Api.Identity;

namespace Trackr.Api.Time;

/// <summary>
/// Decides what "today" means, and turns a calendar day into the instants a query can use.
/// </summary>
/// <remarks>
/// CLAUDE.md section 9.13 makes this load-bearing: the stats views total a <em>local</em> day
/// while the server stores UTC, so the day boundary must live in exactly one helper long before
/// per-user time zones exist. Every aggregate goes through here.
/// <para>
/// <strong>Every method takes a <see cref="TrackrUser"/> even though today it ignores one.</strong>
/// That is the entire point: when milestone 13 adds a per-user zone, the change is one line inside
/// <see cref="ZoneFor"/> and every call site already passes what it needs. A signature without the
/// user would force exactly the rewrite this design exists to prevent.
/// </para>
/// <para>
/// Note the zone is a property of the <em>user</em> rather than of the request. The phone knows
/// its own zone and sending it would be the tempting shortcut, but the server is what aggregates,
/// and two devices in different places must not disagree about which day a meal belongs to.
/// </para>
/// </remarks>
public sealed class DayBoundary(TimeProvider time)
{
    /// <summary>
    /// The zone a user's days are measured in. UTC, for now.
    /// </summary>
    /// <remarks>
    /// This is the one place both a server-wide setting and milestone 13's
    /// <c>TrackrUser.TimeZoneId</c> will land. There is deliberately no configuration knob yet:
    /// section 9.13 permits "UTC or a single configured server zone", and a
    /// <c>TRACKR_TIMEZONE</c> variable would oblige <c>wiki/Configuration.md</c> and
    /// <c>docker/.env.example</c> - both enforced by Trackr.Docs.Tests - for a setting nothing can
    /// visibly use until the stats views exist.
    /// <para>
    /// One thing for milestone 13 to check rather than discover: this project sets
    /// <c>InvariantGlobalization</c>, and resolving a named zone needs the tz database to be
    /// present in the runtime image. <see cref="TimeZoneInfo.Utc"/> needs neither.
    /// </para>
    /// </remarks>
    public TimeZoneInfo ZoneFor(TrackrUser user) => TimeZoneInfo.Utc;

    /// <summary>The calendar day it currently is, for this user.</summary>
    public DateOnly TodayFor(TrackrUser user) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), ZoneFor(user)).DateTime);

    /// <summary>The instants bounding one local day.</summary>
    public (DateTimeOffset FromInclusive, DateTimeOffset ToExclusive) DayFor(
        TrackrUser user,
        DateOnly day) =>
        RangeFor(user, day, day);

    /// <summary>
    /// The instants bounding a run of local days, from the start of the first to the start of the
    /// day after the last.
    /// </summary>
    /// <remarks>
    /// <strong>Half-open, always <c>[from, to)</c>.</strong> Never <c>BETWEEN</c> with an inclusive
    /// end: Postgres keeps microseconds, so an end of <c>23:59:59.999999</c> silently drops
    /// anything logged in the last microsecond of the day. One index on
    /// <c>(UserId, LoggedUtc)</c> serves day, week and month through this one shape.
    /// </remarks>
    public (DateTimeOffset FromInclusive, DateTimeOffset ToExclusive) RangeFor(
        TrackrUser user,
        DateOnly fromDay,
        DateOnly toDayInclusive)
    {
        var zone = ZoneFor(user);

        return (StartOfDay(fromDay, zone), StartOfDay(toDayInclusive.AddDays(1), zone));
    }

    /// <summary>The UTC instant a local day begins.</summary>
    /// <remarks>
    /// The awkward case is a day whose local midnight does not exist, which happens wherever a
    /// zone springs forward at midnight - real in Brazil, Chile and Cuba, and a throw from
    /// <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/> rather than a wrong
    /// answer. It cannot fire while the zone is UTC, which is exactly why it is handled here and
    /// now: this stays correct on the day the zone becomes configurable, without anyone having to
    /// remember it.
    /// </remarks>
    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);

        // Step to the first instant that does exist. Every real DST gap is a whole number of
        // quarter hours, so this needs no adjustment-rule arithmetic; the bound keeps a zone with
        // an implausible rule from spinning.
        for (var step = 0; step < 16 && zone.IsInvalidTime(local); step++)
        {
            local = local.AddMinutes(15);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
