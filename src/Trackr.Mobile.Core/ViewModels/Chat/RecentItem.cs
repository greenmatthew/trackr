using System.Globalization;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.ViewModels.Chat;

/// <summary>
/// One row in "had it again": a food this account confirmed before.
/// </summary>
/// <remarks>
/// Wraps the analysed item rather than flattening it, so tapping a row hands the chat exactly the
/// shape it already knows how to draw and confirm. The formatting lives here for the same reason
/// <c>NutrientRow</c>'s does - a phrase like "2 days ago" is a display decision, and putting it in
/// the XAML would put it somewhere no test can reach.
/// </remarks>
public sealed class RecentItem(RecentItemResponse response, DateTimeOffset now)
{
    public MealAnalysisItem Item { get; } = response.Item;

    public string Name { get; } = response.Item.Name;

    public string? Brand { get; } = response.Item.Brand;

    public bool HasBrand { get; } = !string.IsNullOrWhiteSpace(response.Item.Brand);

    /// <summary>"312 kcal · 2 days ago", or with a count when it has been eaten more than once.</summary>
    public string Summary { get; } = Describe(response, now);

    private static string Describe(RecentItemResponse response, DateTimeOffset now)
    {
        var energy = string.Create(
            CultureInfo.InvariantCulture,
            $"{response.Item.EnergyKcal:0.#} kcal");

        var when = Ago(now - response.LastLoggedUtc);

        return response.TimesLogged > 1
            ? $"{energy} · {when} · logged {response.TimesLogged} times"
            : $"{energy} · {when}";
    }

    /// <remarks>
    /// Coarse on purpose. The question a person is answering is "is this the one I had", and to that
    /// "yesterday" is a better answer than a timestamp.
    /// </remarks>
    private static string Ago(TimeSpan elapsed) => elapsed switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} min ago",
        { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} h ago",
        { TotalDays: < 2 } => "yesterday",
        { TotalDays: < 14 } => $"{(int)elapsed.TotalDays} days ago",
        { TotalDays: < 60 } => $"{(int)(elapsed.TotalDays / 7)} weeks ago",
        _ => $"{(int)(elapsed.TotalDays / 30)} months ago"
    };
}
