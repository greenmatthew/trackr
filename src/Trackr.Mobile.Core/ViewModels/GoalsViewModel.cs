using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Setting the daily targets.
/// </summary>
/// <remarks>
/// A form, and allowed to be one. CLAUDE.md section 10 forbids forms as the way food is
/// <em>logged</em>; this is a settings screen, and section 9.12 requires a hand-typed target to
/// work whether or not anything ever suggests one.
/// <para>
/// Reached from Home rather than made a fourth tab. Three tabs are the shape of the app
/// (AppShell.xaml) and a target is something set occasionally and then left alone.
/// </para>
/// </remarks>
public sealed partial class GoalsViewModel(ITrackrApiClient api, NutrientCatalogCache nutrients)
    : ObservableObject
{
    private IReadOnlyList<NutrientResponse> catalog = [];

    public ObservableCollection<GoalEditRow> Rows { get; } = [];

    /// <summary>Nutrient names, in catalog order, for a row's picker.</summary>
    public ObservableCollection<string> NutrientNames { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? Problem { get; set; }

    [ObservableProperty]
    public partial bool IsSaved { get; set; }

    public bool CanSave => !IsBusy && Rows.All(row => row.IsValid);

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        Problem = null;

        try
        {
            var byKey = await nutrients.EnsureLoadedAsync();

            if (byKey is null)
            {
                Problem = "Could not reach the server, so there is nothing to choose from yet.";

                return;
            }

            catalog = [.. byKey.Values.OrderBy(nutrient => nutrient.SortOrder)];

            NutrientNames.Clear();

            foreach (var nutrient in catalog)
            {
                NutrientNames.Add($"{nutrient.DisplayName} ({NutrientRows.UnitSymbol(nutrient.Unit)})");
            }

            var goals = await api.GetGoalsAsync();

            if (goals is null)
            {
                Problem = "Could not reach the server, so your targets are not known.";

                return;
            }

            Rows.Clear();

            foreach (var goal in goals)
            {
                var index = IndexOf(goal.NutrientKey);

                if (index < 0)
                {
                    // A target for something this server has stopped tracking. Skipped rather than
                    // shown as a blank row, and it survives untouched unless the user saves - which
                    // would drop it, since a save replaces the whole set.
                    continue;
                }

                Rows.Add(new GoalEditRow(this, index, goal.Target, goal.Kind));
            }

            Recheck();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <remarks>
    /// Defaults to the first nutrient not already spoken for, so adding two rows in a row does not
    /// produce two targets for the same thing - which the server refuses, correctly, and which
    /// would be a confusing way to find that out.
    /// </remarks>
    [RelayCommand]
    private void Add()
    {
        var taken = Rows.Select(row => row.NutrientIndex).ToHashSet();
        var free = Enumerable.Range(0, catalog.Count).FirstOrDefault(index => !taken.Contains(index), -1);

        if (free < 0)
        {
            return;
        }

        Rows.Add(new GoalEditRow(this, free, 0m, GoalKind.AtLeast));
        Recheck();
    }

    [RelayCommand]
    private void Remove(GoalEditRow? row)
    {
        if (row is not null)
        {
            Rows.Remove(row);
            Recheck();
        }
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        IsBusy = true;
        Problem = null;
        IsSaved = false;

        try
        {
            var request = new SaveGoalsRequest
            {
                Goals = [.. Rows.Select(row => new SaveGoalRequest
                {
                    NutrientKey = catalog[row.NutrientIndex].Key,
                    Target = row.Target,
                    Kind = row.Kind
                })]
            };

            var saved = await api.SaveGoalsAsync(request);

            if (saved is null)
            {
                Problem = "Your targets could not be saved. Nothing has changed.";

                return;
            }

            IsSaved = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal void Recheck()
    {
        OnPropertyChanged(nameof(CanSave));

        IsSaved = false;
    }

    private int IndexOf(string key)
    {
        for (var index = 0; index < catalog.Count; index++)
        {
            if (string.Equals(catalog[index].Key, key, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSave));
}

/// <summary>
/// One editable target.
/// </summary>
/// <remarks>
/// The amount is bound as text and parsed here, the way the confirmation card's figures are, so an
/// unreadable entry blocks the save rather than quietly becoming zero.
/// </remarks>
public sealed partial class GoalEditRow : ObservableObject
{
    private readonly GoalsViewModel owner;

    public GoalEditRow(GoalsViewModel owner, int nutrientIndex, decimal target, GoalKind kind)
    {
        this.owner = owner;

        NutrientIndex = nutrientIndex;
        TargetText = target > 0 ? target.ToString("0.####", CultureInfo.CurrentCulture) : string.Empty;
        IsCeiling = kind is GoalKind.AtMost;
    }

    [ObservableProperty]
    public partial int NutrientIndex { get; set; }

    [ObservableProperty]
    public partial string TargetText { get; set; } = string.Empty;

    /// <summary>At most, rather than at least.</summary>
    [ObservableProperty]
    public partial bool IsCeiling { get; set; }

    public GoalKind Kind => IsCeiling ? GoalKind.AtMost : GoalKind.AtLeast;

    public decimal Target => TryRead(TargetText, out var value) ? value : 0m;

    public bool IsValid => Target > 0m;

    partial void OnTargetTextChanged(string value) => owner.Recheck();

    partial void OnNutrientIndexChanged(int value) => owner.Recheck();

    partial void OnIsCeilingChanged(bool value) => owner.Recheck();

    private static bool TryRead(string? text, out decimal value)
    {
        value = 0m;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        const NumberStyles Styles = NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands
            | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

        return (decimal.TryParse(text, Styles, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(text, Styles, CultureInfo.InvariantCulture, out value))
            && value >= 0m;
    }
}
