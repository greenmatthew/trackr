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
    TimeProvider time) : ObservableObject
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

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool HasAnything => Stats?.DaysLogged > 0;

    public string Headline => Stats is null
        ? string.Empty
        : $"{NutrientRows.Format(Stats.AveragePerLoggedDay.EnergyKcal)} kcal a day";

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
            var today = DateOnly.FromDateTime(time.GetUtcNow().Date);

            var stats = await api.GetStatsAsync(today.AddDays(-(days - 1)), today);

            if (stats is null)
            {
                Problem = "Could not reach the server, so there is nothing to summarise.";

                return;
            }

            Stats = stats;

            // Scaled against the biggest day rather than against a goal, because goals are
            // milestone 12 and a bar chart with no ceiling has to pick one.
            var tallest = stats.Days.Count == 0 ? 0m : stats.Days.Max(day => day.EnergyKcal);

            Days.Clear();

            foreach (var day in stats.Days)
            {
                Days.Add(new DayBar(day, tallest));
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
/// <param name="Fraction">Zero to one, against the tallest day in the range.</param>
public sealed record DayBar(DateOnly Day, decimal EnergyKcal, double Fraction, string Label)
{
    public DayBar(DayTotals totals, decimal tallest)
        : this(
            totals.Day,
            totals.EnergyKcal,
            tallest <= 0 ? 0d : (double)(totals.EnergyKcal / tallest),
            totals.Day.ToString("d MMM"))
    {
    }

    /// <summary>Whether anything was logged, so a blank day can be drawn as a gap rather than a floor.</summary>
    public bool HasAnything => EnergyKcal > 0m;
}
