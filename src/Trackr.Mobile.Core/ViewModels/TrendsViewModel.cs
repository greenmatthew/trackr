using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// The week and the month: totals, averages and a bar per day.
/// </summary>
/// <remarks>
/// CLAUDE.md section 1 asks for these in "basic form" and means it - rolling summaries and simple
/// trends, not analytics. What matters is that they exist and are honest about the days nobody
/// logged.
/// </remarks>
public sealed partial class TrendsViewModel(
    ITrackrApiClient api,
    NutrientCatalogCache nutrients,
    DisplayUnits units) : ObservableObject
{
    /// <summary>The two ranges offered, in days back from today inclusive.</summary>
    private static readonly (string Label, int Days)[] Ranges =
    [
        ("7 days", 7),
        ("30 days", 30)
    ];

    public IReadOnlyList<string> RangeLabels { get; } = [.. Ranges.Select(range => range.Label)];

    /// <summary>One bar per calendar day, blank days included.</summary>
    public ObservableCollection<DayBar> Days { get; } = [];

    /// <summary>The range's average day, broken down.</summary>
    public ObservableCollection<NutrientRow> Nutrients { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnything))]
    public partial StatsResponse? Stats { get; set; }

    [ObservableProperty]
    public partial int SelectedRange { get; set; }

    /// <summary>
    /// The daily energy target the chart is drawn against, or null when there is none.
    /// </summary>
    /// <remarks>
    /// Energy alone, out of the whole set of targets. This is a chart of one number, and the bars
    /// are kilocalories - a target for sodium has nothing to scale against here.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    [NotifyPropertyChangedFor(nameof(TargetCaption))]
    public partial GoalResponse? EnergyTarget { get; set; }

    public bool HasTarget => EnergyTarget is not null;

    /// <summary>
    /// Says which way the target reads, because the line means opposite things either way.
    /// </summary>
    /// <remarks>
    /// A line drawn without saying whether it is a floor or a ceiling would leave the bars above it
    /// ambiguous - the same picture is a good week or a bad one depending on which it was.
    /// </remarks>
    public string TargetCaption => EnergyTarget is null
        ? string.Empty
        : EnergyTarget.Kind is GoalKind.AtMost
            ? $"Target: at most {units.FormatEnergy(EnergyTarget.Target)} a day"
            : $"Target: at least {units.FormatEnergy(EnergyTarget.Target)} a day";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool HasAnything => Stats?.DaysLogged > 0;

    public string Headline => Stats is null
        ? string.Empty
        : $"{units.FormatEnergy(Stats.AveragePerLoggedDay.EnergyKcal)} a day";

    /// <summary>
    /// Says what the average is an average <em>of</em>, which is the whole honesty of the number.
    /// </summary>
    /// <remarks>
    /// The server divides by the days that have something on them rather than by the length of the
    /// range, so a screen that showed the figure without saying so would be quietly implying a
    /// fuller week than there was.
    /// </remarks>
    public string Detail => Stats is null
        ? string.Empty
        : $"averaged over {Stats.DaysLogged} day{(Stats.DaysLogged == 1 ? "" : "s")} logged "
            + $"of {Stats.Days.Count}";

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Problem = null;

        try
        {
            var catalog = await nutrients.EnsureLoadedAsync();

            var days = Ranges[Math.Clamp(SelectedRange, 0, Ranges.Length - 1)].Days;

            // A window rather than two dates. The phone must not name a day: the server's idea of
            // today follows the account's time zone, and the two disagree for most of every evening
            // - which on the emulator showed up as Home reporting the 19th while this charted the
            // 14th to the 20th, both of them faithful to a different clock.
            var stats = await api.GetRecentStatsAsync(days);

            if (stats is null)
            {
                Problem = "Could not reach the server, so there is nothing to summarise.";

                return;
            }

            Stats = stats;

            // The targets themselves rather than progress against them: progress is measured on one
            // day, and this is a chart of thirty. Null when the server could not be asked, which
            // draws the chart the way it drew before there were targets rather than claiming there
            // are none.
            EnergyTarget = (await api.GetGoalsAsync())
                ?.FirstOrDefault(goal => goal.NutrientKey == CoreNutrients.EnergyKcal);

            var tallest = stats.Days.Count == 0 ? 0m : stats.Days.Max(day => day.EnergyKcal);

            // The taller of the two, so both stay on the chart. Scaling to the target alone hides
            // by how much a day went over; scaling to the tallest day alone puts the target line at
            // the very top of a week that never came near it, which reads as "target reached".
            var ceiling = EnergyTarget is null ? tallest : Math.Max(tallest, EnergyTarget.Target);

            Days.Clear();

            foreach (var day in stats.Days)
            {
                Days.Add(new DayBar(day, ceiling, EnergyTarget));
            }

            Nutrients.Clear();

            foreach (var row in NutrientRows.Build(stats.AveragePerLoggedDay.Nutrients, catalog))
            {
                Nutrients.Add(row);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnStatsChanged(StatsResponse? value)
    {
        OnPropertyChanged(nameof(Headline));
        OnPropertyChanged(nameof(Detail));
    }

    /// <remarks>
    /// Reloading rather than filtering what is held: a longer range is more days than the server
    /// was asked for, not a subset of them.
    /// </remarks>
    partial void OnSelectedRangeChanged(int value) => _ = RefreshAsync();
}

/// <summary>
/// One day's bar: how tall it is, and what it says.
/// </summary>
/// <remarks>
/// A fraction rather than a pixel height, because how tall the chart is belongs to the layout and
/// this project has no charting package - two dozen scaled rectangles is a smaller thing to own
/// than a dependency, and "basic" is what the brief asked for.
/// </remarks>
/// <param name="Fraction">Zero to one, against the chart's ceiling.</param>
/// <param name="TargetFraction">
/// Where the target line sits, on the same scale. Zero when there is no target - the same value
/// every bar in a range carries, because the line is one line across the chart.
/// </param>
/// <param name="IsOver">Past a ceiling. The one combination that is bad news.</param>
/// <param name="IsDone">A floor reached. Named as on Home, because it means the same thing.</param>
public sealed record DayBar(
    DateOnly Day,
    decimal EnergyKcal,
    double Fraction,
    string Label,
    double TargetFraction = 0d,
    bool IsOver = false,
    bool IsDone = false)
{
    public DayBar(DayTotals totals, decimal ceiling, GoalResponse? target = null)
        : this(
            totals.Day,
            totals.EnergyKcal,
            ceiling <= 0 ? 0d : (double)(totals.EnergyKcal / ceiling),
            totals.Day.ToString("d MMM"),
            ceiling <= 0 || target is null ? 0d : (double)(target.Target / ceiling),
            // A day nobody logged is neither, whichever way the target reads. Nothing eaten sits
            // under every ceiling, and drawing that as a day within target would be scoring a blank.
            target is { Kind: GoalKind.AtMost } && totals.HasAnything && totals.EnergyKcal > target.Target,
            target is { Kind: GoalKind.AtLeast } && totals.HasAnything && totals.EnergyKcal >= target.Target)
    {
    }

    /// <summary>Whether anything was logged, so a blank day can be drawn as a gap rather than a floor.</summary>
    public bool HasAnything => EnergyKcal > 0m;

    /// <summary>Whether this column draws part of the target line.</summary>
    public bool HasTarget => TargetFraction > 0d;
}
