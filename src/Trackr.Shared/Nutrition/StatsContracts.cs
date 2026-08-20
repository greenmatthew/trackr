namespace Trackr.Shared.Nutrition;

/// <summary>
/// What was eaten over a run of local days, and on each of them.
/// </summary>
/// <remarks>
/// Milestone 11's output surface. One shape covers today, this week and this month, because they
/// differ only in how many days were asked for - the same reasoning that gave <c>GET /api/log</c>
/// one <c>from</c>/<c>to</c> pair instead of three routes.
/// <para>
/// Everything here is summed from the snapshots on log items and never from the catalog. A later
/// correction to a shared product must not change what a chart said last week, which is the whole
/// reason those snapshots exist.
/// </para>
/// </remarks>
/// <param name="Days">
/// Every calendar day in the range, including the ones with nothing on them. A chart needs the
/// gaps: a week with two blank days is a different picture from a week with two missing bars.
/// </param>
/// <param name="Total">The whole range added up.</param>
/// <param name="AveragePerLoggedDay">
/// The total divided by <paramref name="DaysLogged"/>, not by the length of the range.
/// </param>
/// <param name="DaysLogged">How many days in the range have anything on them.</param>
public sealed record StatsResponse(
    DateOnly From,
    DateOnly To,
    int DaysLogged,
    DayTotals Total,
    DayTotals AveragePerLoggedDay,
    IReadOnlyList<DayTotals> Days);

/// <summary>One day's totals, or a whole range's - the shape is the same either way.</summary>
/// <param name="Day">The local day, or the first day of the range for a total.</param>
/// <param name="Entries">How many log entries went into it.</param>
/// <param name="Nutrients">
/// Everything except the core four, keyed as <c>GET /api/nutrients</c> reports it. A nutrient is
/// absent when nothing that day reported it - which is not the same as zero, and is why a day of
/// food nobody described in detail shows a short list rather than a column of noughts.
/// </param>
public sealed record DayTotals(
    DateOnly Day,
    int Entries,
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    IReadOnlyDictionary<string, decimal> Nutrients)
{
    /// <summary>A day with nothing logged. Zero here really is zero: nothing was eaten.</summary>
    public static DayTotals Empty(DateOnly day) =>
        new(day, 0, 0m, 0m, 0m, 0m, new Dictionary<string, decimal>(StringComparer.Ordinal));

    /// <summary>Whether anything was logged at all, so a client need not test four numbers.</summary>
    public bool HasAnything => Entries > 0;
}
