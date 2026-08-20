using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Today so far - the output surface, and the tab the app opens on.
/// </summary>
/// <remarks>
/// CLAUDE.md section 1 calls this core rather than polish: logging a meal and immediately seeing
/// the day move is the loop the whole app is built around, which is why it is a tab beside the
/// chat rather than something behind a menu.
/// <para>
/// The date shown is the phone's and is for display only. <strong>What counts as "today" for a
/// total is the server's business</strong>, because the server aggregates and the day boundary
/// follows the account's time zone rather than whichever zone the phone is in - section 9.13. So
/// the fetch below sends no dates at all, and a phone that crosses a border does not silently
/// redraw somebody's day.
/// </para>
/// </remarks>
public sealed partial class HomeViewModel(
    ITrackrApiClient api,
    NutrientCatalogCache nutrients,
    INavigationService navigation) : ObservableObject
{
    /// <summary>
    /// The day being totalled, as the server understands it.
    /// </summary>
    /// <remarks>
    /// The server's day rather than the phone's, and not merely for tidiness. The phone's date and
    /// the account's day disagree for most of every evening, and a screen that took its heading
    /// from one clock and its numbers from another would report today's total under yesterday's
    /// name - which the emulator duly showed, with Home saying the 19th while Trends charted up to
    /// the 20th.
    /// <para>
    /// Falls back to the phone's date only before the first answer arrives, where a blank heading
    /// would be worse than an approximate one.
    /// </para>
    /// </remarks>
    public string Today => (Totals?.Day.ToDateTime(TimeOnly.MinValue) ?? DateTime.Now)
        .ToString("dddd d MMMM");

    /// <summary>Micronutrients something reported today, in label order.</summary>
    public ObservableCollection<NutrientRow> Nutrients { get; } = [];

    /// <summary>
    /// Daily targets against today, drawn above the totals.
    /// </summary>
    /// <remarks>
    /// Empty until somebody sets one, and empty is a perfectly good state: CLAUDE.md's closing note
    /// is that tracking is a tool, and an app that demanded targets before it would show a number
    /// would be the version of this that drives anxiety rather than helping.
    /// </remarks>
    public ObservableCollection<GoalRow> Goals { get; } = [];

    public bool HasGoals => Goals.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnything))]
    public partial DayTotals? Totals { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    /// <summary>Why there is nothing to show, when it is not simply that nothing was eaten.</summary>
    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool HasAnything => Totals?.HasAnything is true;

    public string EnergyText => Format(Totals?.EnergyKcal ?? 0m);

    public string FatText => $"{Format(Totals?.FatG ?? 0m)} g";

    public string CarbohydrateText => $"{Format(Totals?.CarbohydrateG ?? 0m)} g";

    public string ProteinText => $"{Format(Totals?.ProteinG ?? 0m)} g";

    /// <summary>
    /// Fetches the day. Called every time the tab appears, not once.
    /// </summary>
    /// <remarks>
    /// Confirming a meal happens on another tab, so a total fetched once would be stale by exactly
    /// the moment somebody looks at it to see what they just logged.
    /// </remarks>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsLoading = true;
        Problem = null;

        try
        {
            var catalog = await nutrients.EnsureLoadedAsync();
            var stats = await api.GetStatsAsync();
            var goals = await api.GetGoalProgressAsync();

            if (stats is null)
            {
                // Never an empty day: "you have eaten nothing" and "the server did not answer" look
                // identical if you let them, and only one of them is the user's fault.
                Problem = "Could not reach the server, so today's total is not known.";

                return;
            }

            Totals = stats.Total;

            Nutrients.Clear();

            foreach (var row in NutrientRows.Build(stats.Total.Nutrients, catalog))
            {
                Nutrients.Add(row);
            }

            Goals.Clear();

            foreach (var goal in goals ?? [])
            {
                Goals.Add(new GoalRow(goal, catalog));
            }

            OnPropertyChanged(nameof(HasGoals));
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnTotalsChanged(DayTotals? value)
    {
        OnPropertyChanged(nameof(Today));
        OnPropertyChanged(nameof(EnergyText));
        OnPropertyChanged(nameof(FatText));
        OnPropertyChanged(nameof(CarbohydrateText));
        OnPropertyChanged(nameof(ProteinText));
    }

    /// <remarks>
    /// A route rather than a fourth tab: three tabs are the shape of the app, and a target is set
    /// occasionally and then left alone.
    /// </remarks>
    [RelayCommand]
    private Task OpenGoalsAsync() => navigation.GoToGoalsAsync();

    private static string Format(decimal value) => NutrientRows.Format(value);
}
