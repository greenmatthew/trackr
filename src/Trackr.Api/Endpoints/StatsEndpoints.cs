using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Time;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// The output surface: what was eaten, added up.
/// </summary>
/// <remarks>
/// CLAUDE.md section 1 pairs the chat with an always-visible picture of the day. This is the half
/// the phone reads from, and it is deliberately one route: today, this week and this month differ
/// only in how many days were asked for.
/// <para>
/// <strong>Everything is summed from log item snapshots, never from the catalog.</strong> A later
/// correction to a shared product must not change what last week's chart said, which is the whole
/// reason those snapshots exist - <c>LogItem.FoodItemId</c> is provenance and nothing joins
/// through it for a number.
/// </para>
/// </remarks>
public static class StatsEndpoints
{
    /// <summary>
    /// The same cap the log itself uses, and for the same reason.
    /// </summary>
    /// <remarks>
    /// A year and a day, so "the last twelve months" fits. Without it one mistyped date turns the
    /// month view into an all-time scan of every nutrient row the account owns.
    /// </remarks>
    private const int MaxDaysInRange = 366;

    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/stats", GetStatsAsync)
            .WithName("GetStats")
            .WithSummary("Totals per local day, plus the range's total and its average. Defaults to today.");

        return app;
    }

    /// <param name="from">First local day to include. Defaults to today.</param>
    /// <param name="to">Last local day, inclusive. Defaults to <paramref name="from"/>.</param>
    private static async Task<IResult> GetStatsAsync(
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        DayBoundary days,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var firstDay = from ?? days.TodayFor(user);
        var lastDay = to ?? firstDay;

        if (lastDay < firstDay)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["The end of the range comes before its start."]
            });
        }

        if (lastDay.DayNumber - firstDay.DayNumber + 1 > MaxDaysInRange)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = [$"That is more than {MaxDaysInRange} days at once."]
            });
        }

        var (fromUtc, toUtc) = days.RangeFor(user, firstDay, lastDay);
        var zone = days.ZoneFor(user);

        // Item rows with their entry's timestamp and their nutrient map. Grouped by local day in
        // memory rather than in SQL: which day an instant belongs to is a question about a time
        // zone, and DayBoundary is the one place allowed to answer it - a date_trunc here would be
        // a second answer, free to disagree the moment section 9.13 makes the zone per-user.
        var rows = await db.LogItems
            .Where(item => item.LogEntry!.UserId == user.Id
                && item.LogEntry.LoggedUtc >= fromUtc
                && item.LogEntry.LoggedUtc < toUtc)
            .Select(item => new StatRow(
                item.LogEntry!.LoggedUtc,
                item.LogEntryId,
                item.EnergyKcal,
                item.FatG,
                item.CarbohydrateG,
                item.ProteinG,
                item.Nutrients
                    .Select(nutrient => new StatNutrient(nutrient.NutrientKey, nutrient.Amount))
                    .ToList()))
            .ToListAsync(cancellationToken);

        var byDay = rows
            .GroupBy(row => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.LoggedUtc, zone).DateTime))
            .ToDictionary(group => group.Key, group => Total(group.Key, group));

        // Every calendar day, including the blank ones. A week with two days of nothing is a
        // different picture from a week with two bars missing, and only the client drawing it can
        // tell those apart if the server sends both the same way.
        var everyDay = new List<DayTotals>(lastDay.DayNumber - firstDay.DayNumber + 1);

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            everyDay.Add(byDay.TryGetValue(day, out var totals) ? totals : DayTotals.Empty(day));
        }

        var whole = Total(firstDay, rows);
        var logged = byDay.Count;

        return Results.Ok(new StatsResponse(
            firstDay,
            lastDay,
            logged,
            whole,
            Average(firstDay, whole, logged),
            everyDay));
    }

    private static DayTotals Total(DateOnly day, IEnumerable<StatRow> rows)
    {
        var materialised = rows as IReadOnlyCollection<StatRow> ?? [.. rows];

        var nutrients = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var nutrient in materialised.SelectMany(row => row.Nutrients))
        {
            // Absent stays absent. A nutrient nobody reported is "not measured", and seeding the
            // map with zeroes would turn a day of undescribed food into a confident column of
            // noughts - the distinction wiki/Nutrient-Reference.md exists to protect.
            nutrients[nutrient.Key] = nutrients.GetValueOrDefault(nutrient.Key) + nutrient.Amount;
        }

        return new DayTotals(
            day,
            materialised.Select(row => row.LogEntryId).Distinct().Count(),
            StoredPrecision.Amount(materialised.Sum(row => row.EnergyKcal)),
            StoredPrecision.Amount(materialised.Sum(row => row.FatG)),
            StoredPrecision.Amount(materialised.Sum(row => row.CarbohydrateG)),
            StoredPrecision.Amount(materialised.Sum(row => row.ProteinG)),
            nutrients.ToDictionary(
                nutrient => nutrient.Key,
                nutrient => StoredPrecision.Amount(nutrient.Value),
                StringComparer.Ordinal));
    }

    /// <summary>
    /// The total divided by the days that have something on them.
    /// </summary>
    /// <remarks>
    /// Not by the length of the range, and the difference matters on exactly the range people look
    /// at most. Three days logged out of seven, divided by seven, reports an average nobody ate and
    /// makes a partly-filled week look like a fast. Divided by three it answers the question the
    /// number is actually asked for: what a day looks like when it is recorded.
    /// </remarks>
    private static DayTotals Average(DateOnly day, DayTotals total, int daysLogged)
    {
        if (daysLogged <= 1)
        {
            return total with { Day = day };
        }

        return new DayTotals(
            day,
            total.Entries,
            StoredPrecision.Amount(total.EnergyKcal / daysLogged),
            StoredPrecision.Amount(total.FatG / daysLogged),
            StoredPrecision.Amount(total.CarbohydrateG / daysLogged),
            StoredPrecision.Amount(total.ProteinG / daysLogged),
            total.Nutrients.ToDictionary(
                nutrient => nutrient.Key,
                nutrient => StoredPrecision.Amount(nutrient.Value / daysLogged),
                StringComparer.Ordinal));
    }

    private sealed record StatNutrient(string Key, decimal Amount);

    private sealed record StatRow(
        DateTimeOffset LoggedUtc,
        Guid LogEntryId,
        decimal EnergyKcal,
        decimal FatG,
        decimal CarbohydrateG,
        decimal ProteinG,
        List<StatNutrient> Nutrients);
}
