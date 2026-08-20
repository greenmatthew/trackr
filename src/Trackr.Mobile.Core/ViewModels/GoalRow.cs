using Trackr.Mobile.Core.Nutrition;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// One target on screen: what it is, how the day is going, and which way that reads.
/// </summary>
/// <remarks>
/// The direction is the whole of it. A floor passed is the point; a ceiling passed is the problem.
/// Drawing both as "100% complete" would congratulate somebody for going over their calories.
/// </remarks>
public sealed class GoalRow
{
    public GoalRow(GoalProgressResponse progress, IReadOnlyDictionary<string, NutrientResponse>? catalog)
    {
        var nutrient = catalog?.GetValueOrDefault(progress.NutrientKey);
        var unit = nutrient is null ? string.Empty : NutrientRows.UnitSymbol(nutrient.Unit);

        // The key is a poor label but a better one than a blank row, and the catalog is only
        // missing when the server could not be asked for it.
        DisplayName = nutrient?.DisplayName ?? progress.NutrientKey;

        Amount = string.IsNullOrEmpty(unit)
            ? $"{NutrientRows.Format(progress.Consumed)} of {NutrientRows.Format(progress.Target)}"
            : $"{NutrientRows.Format(progress.Consumed)} of {NutrientRows.Format(progress.Target)} {unit}";

        IsMet = progress.IsMet;
        IsCeiling = progress.Kind is GoalKind.AtMost;

        // Clamped for the bar only. The fraction itself is uncapped on the wire so that "over" is
        // expressible, and IsExceeded below is what carries it.
        Fraction = Math.Clamp(progress.Fraction, 0d, 1d);
        IsExceeded = progress.Fraction > 1d;
    }

    public string DisplayName { get; }

    /// <summary>"80 of 100 g".</summary>
    public string Amount { get; }

    /// <summary>How full the bar is, zero to one.</summary>
    public double Fraction { get; }

    public bool IsMet { get; }

    public bool IsCeiling { get; }

    /// <summary>Past the target, whether that is good news or bad.</summary>
    public bool IsExceeded { get; }

    /// <summary>
    /// Whether to draw this as a warning: a ceiling that has been passed.
    /// </summary>
    /// <remarks>
    /// The only combination that is bad news. A floor not yet reached is a day in progress rather
    /// than a failure, which is what keeps a progress bar from being an accusation at breakfast.
    /// </remarks>
    public bool IsOver => IsCeiling && IsExceeded;

    /// <summary>Whether to draw this as done: a floor that has been reached.</summary>
    public bool IsDone => !IsCeiling && IsMet;
}
