using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Setting the daily targets - a settings form, which section 10 permits and section 9.12 requires.
/// </summary>
public sealed class GoalsViewModelTests
{
    [Fact]
    public async Task Existing_targets_are_loaded_for_editing()
    {
        var (goals, api) = Build();

        api.GetGoalsAsync(Arg.Any<CancellationToken>())
            .Returns([new GoalResponse("protein", 100m, GoalKind.AtLeast)]);

        await goals.LoadCommand.ExecuteAsync(null);

        var row = Assert.Single(goals.Rows);

        Assert.Equal("100", row.TargetText);
        Assert.False(row.IsCeiling);
        Assert.True(goals.CanSave);
    }

    /// <remarks>
    /// Parsed here rather than defaulted, the way the confirmation card's figures are: an
    /// unreadable amount blocks the save instead of quietly becoming zero.
    /// </remarks>
    [Fact]
    public async Task An_unreadable_amount_blocks_the_save()
    {
        var (goals, api) = Build();

        api.GetGoalsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await goals.LoadCommand.ExecuteAsync(null);

        goals.AddCommand.Execute(null);

        Assert.False(goals.CanSave);

        goals.Rows[0].TargetText = "not a number";

        Assert.False(goals.CanSave);

        goals.Rows[0].TargetText = "120";

        Assert.True(goals.CanSave);
    }

    /// <remarks>
    /// The server refuses two targets for one nutrient, correctly, and finding that out by way of a
    /// rejected save would be a confusing way to learn it.
    /// </remarks>
    [Fact]
    public async Task Adding_twice_does_not_pick_the_same_nutrient_twice()
    {
        var (goals, api) = Build();

        api.GetGoalsAsync(Arg.Any<CancellationToken>()).Returns([]);

        await goals.LoadCommand.ExecuteAsync(null);

        goals.AddCommand.Execute(null);
        goals.AddCommand.Execute(null);

        Assert.Equal(2, goals.Rows.Count);
        Assert.NotEqual(goals.Rows[0].NutrientIndex, goals.Rows[1].NutrientIndex);
    }

    [Fact]
    public async Task Saving_sends_the_whole_set_with_its_directions()
    {
        var (goals, api) = Build();

        api.GetGoalsAsync(Arg.Any<CancellationToken>())
            .Returns([new GoalResponse("energy_kcal", 2_000m, GoalKind.AtMost)]);
        api.SaveGoalsAsync(Arg.Any<SaveGoalsRequest>(), Arg.Any<CancellationToken>())
            .Returns([new GoalResponse("energy_kcal", 1_800m, GoalKind.AtMost)]);

        await goals.LoadCommand.ExecuteAsync(null);

        goals.Rows[0].TargetText = "1800";

        await goals.SaveCommand.ExecuteAsync(null);

        await api.Received(1).SaveGoalsAsync(
            Arg.Is<SaveGoalsRequest>(request =>
                request.Goals.Count == 1
                && request.Goals[0].NutrientKey == "energy_kcal"
                && request.Goals[0].Target == 1_800m
                && request.Goals[0].Kind == GoalKind.AtMost),
            Arg.Any<CancellationToken>());

        Assert.True(goals.IsSaved);
    }

    [Fact]
    public async Task A_save_that_failed_says_so_and_does_not_claim_to_have_saved()
    {
        var (goals, api) = Build();

        api.GetGoalsAsync(Arg.Any<CancellationToken>())
            .Returns([new GoalResponse("protein", 100m, GoalKind.AtLeast)]);
        api.SaveGoalsAsync(Arg.Any<SaveGoalsRequest>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<GoalResponse>?)null);

        await goals.LoadCommand.ExecuteAsync(null);
        await goals.SaveCommand.ExecuteAsync(null);

        Assert.False(goals.IsSaved);
        Assert.Contains("could not be saved", goals.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreachable_server_offers_nothing_to_edit()
    {
        var (goals, api) = Build();

        api.GetGoalsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<GoalResponse>?)null);

        await goals.LoadCommand.ExecuteAsync(null);

        Assert.Empty(goals.Rows);
        Assert.Contains("Could not reach the server", goals.Problem!, StringComparison.Ordinal);
    }

    private static (GoalsViewModel Goals, ITrackrApiClient Api) Build()
    {
        var api = Substitute.For<ITrackrApiClient>();

        api.GetNutrientsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new NutrientResponse("energy_kcal", "Energy", NutrientUnit.Kilocalorie, NutrientGroup.Core, 1, true),
            new NutrientResponse("protein", "Protein", NutrientUnit.Gram, NutrientGroup.Core, 4, true),
            new NutrientResponse("sodium", "Sodium", NutrientUnit.Milligram, NutrientGroup.SterolsAndElectrolytes, 90, false)
        ]);

        return (new GoalsViewModel(api, new NutrientCatalogCache(api)), api);
    }
}
