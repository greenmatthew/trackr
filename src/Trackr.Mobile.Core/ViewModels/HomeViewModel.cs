using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.Storage;
using Trackr.Mobile.Core.Sync;
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
    INavigationService navigation,
    DisplayUnits units,
    OutboxSync outbox) : ObservableObject
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
    /// Meals confirmed on this phone that the server has not taken yet.
    /// </summary>
    /// <remarks>
    /// Home is where this belongs, because Home is where the number it contradicts is. A total that
    /// silently omits three queued meals is a wrong number on the screen the whole app is built
    /// around, and the chat card that queued them is in a transcript that does not survive a
    /// restart. So the queue is shown next to the total it is missing from.
    /// </remarks>
    public ObservableCollection<QueuedMeal> Queued { get; } = [];

    public bool HasQueued => Queued.Count > 0;

    /// <summary>
    /// What the queue amounts to, in a sentence. Null when there is nothing waiting.
    /// </summary>
    /// <remarks>
    /// Says explicitly that today's total does not include them, because that is the thing a person
    /// looking at this screen would otherwise get wrong.
    /// </remarks>
    public string? QueuedSummary => Queued.Count switch
    {
        0 => null,
        1 => "1 meal is waiting to be sent, and is not in today's total.",
        var many => $"{many} meals are waiting to be sent, and are not in today's total."
    };

    /// <summary>Whether anything in the queue has stopped trying by itself.</summary>
    public bool HasStuck => Queued.Any(meal => meal.Rejected);

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

    /// <summary>
    /// The day's energy, in the unit the account reads in.
    /// </summary>
    /// <remarks>
    /// The server reports kilocalories whatever this says; the conversion is on the way to the
    /// screen and nowhere else - see <see cref="DisplayUnits"/>.
    /// </remarks>
    public string EnergyText => units.FormatEnergyAmount(Totals?.EnergyKcal ?? 0m);

    /// <summary>"kcal today", or "kJ today". Bound rather than written into the page.</summary>
    public string EnergyUnitText => $"{units.EnergySymbol} today";

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
            // Before the totals rather than after: a queue that empties now is a total that should
            // already include what it delivered, and asking the other way round shows a stale
            // number beside a banner that has just gone.
            await outbox.FlushAsync();
            await ReadQueueAsync();

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
                Goals.Add(new GoalRow(goal, catalog, units));
            }

            OnPropertyChanged(nameof(HasGoals));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Sends one stuck meal again, from the banner.</summary>
    [RelayCommand]
    private async Task RetryQueuedAsync(QueuedMeal? meal)
    {
        if (meal is null)
        {
            return;
        }

        await outbox.RetryAsync(meal.Id);
        await RefreshAsync();
    }

    /// <summary>
    /// Throws one away, from the banner. The only way a confirmed meal is ever lost, and it takes
    /// a deliberate tap to do it.
    /// </summary>
    [RelayCommand]
    private async Task DiscardQueuedAsync(QueuedMeal? meal)
    {
        if (meal is null)
        {
            return;
        }

        await outbox.DiscardAsync(meal.Id);
        await ReadQueueAsync();
    }

    private async Task ReadQueueAsync()
    {
        var queued = await outbox.ReadAsync();

        Queued.Clear();

        foreach (var meal in queued)
        {
            Queued.Add(meal);
        }

        OnPropertyChanged(nameof(HasQueued));
        OnPropertyChanged(nameof(QueuedSummary));
        OnPropertyChanged(nameof(HasStuck));
    }

    partial void OnTotalsChanged(DayTotals? value)
    {
        OnPropertyChanged(nameof(Today));
        OnPropertyChanged(nameof(EnergyText));
        OnPropertyChanged(nameof(EnergyUnitText));
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
