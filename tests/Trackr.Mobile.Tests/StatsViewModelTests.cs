using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Milestone 11's output surface: today on Home, the week and the month on Trends.
/// </summary>
public sealed class StatsViewModelTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Todays_totals_are_shown_with_the_nutrients_that_were_reported()
    {
        var (home, api) = BuildHome();

        api.GetStatsAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Stats(Day(energyKcal: 1_850m, sodium: 2_100m)));

        await home.RefreshCommand.ExecuteAsync(null);

        Assert.True(home.HasAnything);
        Assert.Equal("1850", home.EnergyText);
        Assert.Equal("70 g", home.FatText);
        Assert.Equal("Sodium", Assert.Single(home.Nutrients).DisplayName);
    }

    /// <remarks>
    /// The heading comes from the same answer as the numbers under it. Taking one from the phone
    /// and the other from the server reports today's total under yesterday's name for most of every
    /// evening, which is what the emulator showed.
    /// </remarks>
    [Fact]
    public void The_heading_names_the_day_the_server_totalled()
    {
        var (home, _) = BuildHome();

        home.Totals = Day(day: new DateOnly(2026, 8, 20));

        Assert.Equal("Thursday 20 August", home.Today);
    }

    /// <remarks>
    /// The days are the account's rather than the phone's: the server aggregates and owns the day
    /// boundary, so a phone in another time zone must not be able to redraw somebody's day.
    /// </remarks>
    [Fact]
    public async Task Today_is_asked_for_without_the_phone_naming_a_date()
    {
        var (home, api) = BuildHome();

        api.GetStatsAsync(null, null, Arg.Any<CancellationToken>()).Returns(Stats(Day()));

        await home.RefreshCommand.ExecuteAsync(null);

        await api.Received(1).GetStatsAsync(null, null, Arg.Any<CancellationToken>());
    }

    /// <remarks>
    /// "You have eaten nothing" and "the server did not answer" look identical if you let them,
    /// and only one of them is the user's doing.
    /// </remarks>
    [Fact]
    public async Task An_unreachable_server_is_not_drawn_as_an_empty_day()
    {
        var (home, api) = BuildHome();

        api.GetStatsAsync(null, null, Arg.Any<CancellationToken>()).Returns((StatsResponse?)null);

        await home.RefreshCommand.ExecuteAsync(null);

        Assert.False(home.HasAnything);
        Assert.Contains("Could not reach the server", home.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_day_with_nothing_on_it_is_an_empty_state_rather_than_a_row_of_zeroes()
    {
        var (home, api) = BuildHome();

        api.GetStatsAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(Stats(DayTotals.Empty(new DateOnly(2026, 8, 19))));

        await home.RefreshCommand.ExecuteAsync(null);

        Assert.False(home.HasAnything);
        Assert.Null(home.Problem);
        Assert.Empty(home.Nutrients);
    }

    /// <summary>
    /// A window, never a pair of dates the phone worked out.
    /// </summary>
    /// <remarks>
    /// The server's today follows the account's time zone, and the phone's does not. They disagree
    /// for most of every evening - which on the emulator showed up as Home reporting the 19th while
    /// Trends charted the 14th to the 20th, each faithful to a different clock.
    /// </remarks>
    [Fact]
    public async Task A_week_is_asked_for_without_the_phone_working_out_which_week()
    {
        var (trends, api) = BuildTrends();

        api.GetRecentStatsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Stats(Day()));

        await trends.RefreshCommand.ExecuteAsync(null);

        await api.Received().GetRecentStatsAsync(7, Arg.Any<CancellationToken>());

        await api.DidNotReceive().GetStatsAsync(
            Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The average is per day logged, and the screen says so.
    /// </summary>
    /// <remarks>
    /// Showing the figure without saying what it is an average of would quietly imply a fuller week
    /// than there was.
    /// </remarks>
    [Fact]
    public async Task The_headline_says_what_the_average_is_an_average_of()
    {
        var (trends, api) = BuildTrends();

        api.GetRecentStatsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Stats(
                Day(energyKcal: 6_000m),
                daysLogged: 3,
                average: Day(energyKcal: 2_000m),
                days: 7));

        await trends.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("2000 kcal a day", trends.Headline);
        Assert.Equal("averaged over 3 days logged of 7", trends.Detail);
    }

    /// <remarks>
    /// A blank day is a fact, not a missing bar - so it takes its place in the row and draws
    /// nothing, rather than being left out and letting the days either side sit next to each other.
    /// </remarks>
    [Fact]
    public async Task Every_day_gets_a_bar_and_the_blank_ones_draw_nothing()
    {
        var (trends, api) = BuildTrends();

        api.GetRecentStatsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Stats(
                Day(energyKcal: 3_000m),
                daysLogged: 2,
                days: 3,
                perDay:
                [
                    Day(energyKcal: 2_000m, day: new DateOnly(2026, 8, 17)),
                    DayTotals.Empty(new DateOnly(2026, 8, 18)),
                    Day(energyKcal: 1_000m, day: new DateOnly(2026, 8, 19))
                ]));

        await trends.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(3, trends.Days.Count);
        Assert.Equal([1d, 0d, 0.5d], trends.Days.Select(bar => bar.Fraction));
        Assert.Equal([true, false, true], trends.Days.Select(bar => bar.HasAnything));
    }

    /// <summary>
    /// A ceiling passed and a floor reached are opposite results and must not draw the same.
    /// </summary>
    /// <remarks>
    /// Drawing both as "done" would congratulate somebody for going over their calories.
    /// </remarks>
    [Fact]
    public async Task A_ceiling_passed_is_a_warning_and_a_floor_reached_is_not()
    {
        var (home, api) = BuildHome();

        api.GetStatsAsync(null, null, Arg.Any<CancellationToken>()).Returns(Stats(Day()));
        api.GetGoalProgressAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new GoalProgressResponse("energy_kcal", 2_000m, GoalKind.AtMost, 2_400m, 1.2d, false),
            new GoalProgressResponse("protein", 100m, GoalKind.AtLeast, 120m, 1.2d, true)
        ]);

        await home.RefreshCommand.ExecuteAsync(null);

        var energy = home.Goals[0];
        var protein = home.Goals[1];

        Assert.True(energy.IsOver);
        Assert.False(energy.IsDone);

        Assert.False(protein.IsOver);
        Assert.True(protein.IsDone);

        // Clamped for the bar; "over" is carried by the flag rather than by a bar past its end.
        Assert.Equal(1d, energy.Fraction);
    }

    /// <remarks>
    /// A floor not yet reached is a day in progress, not a failure - which is what keeps a progress
    /// bar from being an accusation at breakfast.
    /// </remarks>
    [Fact]
    public async Task A_target_not_yet_reached_is_neither_done_nor_a_warning()
    {
        var (home, api) = BuildHome();

        api.GetStatsAsync(null, null, Arg.Any<CancellationToken>()).Returns(Stats(Day()));
        api.GetGoalProgressAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new GoalProgressResponse("protein", 100m, GoalKind.AtLeast, 40m, 0.4d, false)
        ]);

        await home.RefreshCommand.ExecuteAsync(null);

        var protein = Assert.Single(home.Goals);

        Assert.False(protein.IsDone);
        Assert.False(protein.IsOver);
        Assert.Equal("Protein", protein.DisplayName);
        Assert.Equal("40 of 100 g", protein.Amount);
    }

    /// <remarks>
    /// An account with no targets is a perfectly good state. CLAUDE.md's closing note is that
    /// tracking is a tool, and an app that demanded targets before showing a number would be the
    /// version of this that drives anxiety rather than helping.
    /// </remarks>
    [Fact]
    public async Task An_account_with_no_targets_simply_has_none()
    {
        var (home, api) = BuildHome();

        api.GetStatsAsync(null, null, Arg.Any<CancellationToken>()).Returns(Stats(Day()));
        api.GetGoalProgressAsync(Arg.Any<CancellationToken>()).Returns([]);

        await home.RefreshCommand.ExecuteAsync(null);

        Assert.False(home.HasGoals);
        Assert.True(home.HasAnything);
        Assert.Null(home.Problem);
    }

    private static (HomeViewModel Home, ITrackrApiClient Api) BuildHome()
    {
        var api = WithCatalog();

        return (
            new HomeViewModel(api, new NutrientCatalogCache(api), Substitute.For<INavigationService>()),
            api);
    }

    private static (TrendsViewModel Trends, ITrackrApiClient Api) BuildTrends()
    {
        var api = WithCatalog();

        return (new TrendsViewModel(api, new NutrientCatalogCache(api)), api);
    }

    private static ITrackrApiClient WithCatalog()
    {
        var api = Substitute.For<ITrackrApiClient>();

        api.GetNutrientsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new NutrientResponse("sodium", "Sodium", NutrientUnit.Milligram, NutrientGroup.SterolsAndElectrolytes, 90, false),
            new NutrientResponse("protein", "Protein", NutrientUnit.Gram, NutrientGroup.Core, 4, true),
            new NutrientResponse("energy_kcal", "Energy", NutrientUnit.Kilocalorie, NutrientGroup.Core, 1, true)
        ]);

        return api;
    }

    private static DayTotals Day(
        decimal energyKcal = 1_850m,
        decimal? sodium = null,
        DateOnly? day = null) =>
        new(
            day ?? new DateOnly(2026, 8, 19),
            2,
            energyKcal,
            70m,
            180m,
            95m,
            sodium is null
                ? new Dictionary<string, decimal>(StringComparer.Ordinal)
                : new Dictionary<string, decimal>(StringComparer.Ordinal) { ["sodium"] = sodium.Value });

    private static StatsResponse Stats(
        DayTotals total,
        int daysLogged = 1,
        DayTotals? average = null,
        int days = 1,
        IReadOnlyList<DayTotals>? perDay = null) =>
        new(
            new DateOnly(2026, 8, 19).AddDays(-(days - 1)),
            new DateOnly(2026, 8, 19),
            daysLogged,
            total,
            average ?? total,
            perDay ?? [.. Enumerable.Range(0, days).Select(offset =>
                DayTotals.Empty(new DateOnly(2026, 8, 19).AddDays(-(days - 1 - offset))))]);
}
