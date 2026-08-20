using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Milestone 12: daily targets, and how a day is going against them.
/// </summary>
public sealed class GoalTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Targets_are_saved_and_read_back()
    {
        using var client = await RegisterOwnerAsync();

        var saved = await SaveAsync(
            client,
            (CoreNutrients.EnergyKcal, 2_000m, GoalKind.AtMost),
            (CoreNutrients.Protein, 100m, GoalKind.AtLeast));

        Assert.Equal(2, saved.Length);
        Assert.Equal(GoalKind.AtMost, saved.Single(goal => goal.NutrientKey == CoreNutrients.EnergyKcal).Kind);
    }

    /// <remarks>
    /// The core four are catalog rows as well as columns precisely so a target can treat every
    /// nutrient the same way. Adding one for selenium is a row, not a migration.
    /// </remarks>
    [Fact]
    public async Task A_target_may_be_set_for_any_nutrient_the_server_tracks()
    {
        using var client = await RegisterOwnerAsync();

        var saved = await SaveAsync(client, ("sodium", 2_300m, GoalKind.AtMost));

        Assert.Equal("sodium", Assert.Single(saved).NutrientKey);
    }

    [Fact]
    public async Task A_nutrient_the_server_does_not_track_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await PutAsync(client, ("unobtainium", 1m, GoalKind.AtLeast));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("nutrientKey", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// A contradiction rather than a refinement: "at least 100 g" and "at most 80 g" cannot both be
    /// satisfied, so the second is refused rather than allowed to overwrite the first.
    /// </remarks>
    [Fact]
    public async Task Two_targets_for_one_nutrient_are_refused()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await PutAsync(
            client,
            (CoreNutrients.Protein, 100m, GoalKind.AtLeast),
            (CoreNutrients.Protein, 80m, GoalKind.AtMost));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_target_of_nothing_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await PutAsync(client, (CoreNutrients.Protein, 0m, GoalKind.AtLeast));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <remarks>
    /// Wholesale, like the catalog's nutrient map: a merge leaves "stop tracking this one"
    /// inexpressible.
    /// </remarks>
    [Fact]
    public async Task Saving_replaces_the_whole_set_rather_than_merging_it()
    {
        using var client = await RegisterOwnerAsync();

        await SaveAsync(client, (CoreNutrients.Protein, 100m, GoalKind.AtLeast), ("sodium", 2_300m, GoalKind.AtMost));

        var replaced = await SaveAsync(client, (CoreNutrients.Protein, 120m, GoalKind.AtLeast));

        Assert.Equal(120m, Assert.Single(replaced).Target);
    }

    [Fact]
    public async Task Targets_are_the_callers_own_and_nobody_elses()
    {
        using var owner = await RegisterOwnerAsync();
        await SaveAsync(owner, (CoreNutrients.Protein, 100m, GoalKind.AtLeast));

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        Assert.Empty(await GetAsync(member));
    }

    [Fact]
    public async Task Progress_measures_a_target_against_what_was_eaten()
    {
        using var client = await RegisterOwnerAsync();

        await SaveAsync(
            client,
            (CoreNutrients.EnergyKcal, 2_000m, GoalKind.AtMost),
            (CoreNutrients.Protein, 100m, GoalKind.AtLeast));

        await LogAsync(client, Payloads.AdHocLog(energyKcal: 500m, proteinG: 40m, quantity: 2m));

        var progress = await ProgressAsync(client);

        var energy = progress.Single(goal => goal.NutrientKey == CoreNutrients.EnergyKcal);
        var protein = progress.Single(goal => goal.NutrientKey == CoreNutrients.Protein);

        Assert.Equal(1_000m, energy.Consumed);
        Assert.True(energy.IsMet);

        Assert.Equal(80m, protein.Consumed);
        Assert.False(protein.IsMet);
        Assert.Equal(0.8d, protein.Fraction);
    }

    /// <summary>
    /// The two directions read in opposite ways, which is why the direction is stored.
    /// </summary>
    [Fact]
    public async Task Passing_a_ceiling_breaks_it_and_passing_a_floor_meets_it()
    {
        using var client = await RegisterOwnerAsync();

        await SaveAsync(
            client,
            (CoreNutrients.EnergyKcal, 500m, GoalKind.AtMost),
            (CoreNutrients.Protein, 10m, GoalKind.AtLeast));

        await LogAsync(client, Payloads.AdHocLog(energyKcal: 900m, proteinG: 40m));

        var progress = await ProgressAsync(client);

        Assert.False(progress.Single(goal => goal.NutrientKey == CoreNutrients.EnergyKcal).IsMet);
        Assert.True(progress.Single(goal => goal.NutrientKey == CoreNutrients.Protein).IsMet);

        // Uncapped: past a floor is the point and past a ceiling is the problem, and a client that
        // could not see beyond 100% could not draw the difference.
        Assert.Equal(1.8d, progress.Single(goal => goal.NutrientKey == CoreNutrients.EnergyKcal).Fraction);
    }

    /// <remarks>
    /// Not the "missing is not zero" rule being broken. A target asks how much was eaten, and a
    /// nutrient nobody recorded contributed nothing to the day - reporting it as met on the
    /// strength of food nobody described would be the dishonest version.
    /// </remarks>
    [Fact]
    public async Task A_nutrient_nothing_reported_counts_as_nothing_eaten()
    {
        using var client = await RegisterOwnerAsync();

        await SaveAsync(client, ("sodium", 2_300m, GoalKind.AtLeast));
        await LogAsync(client, Payloads.AdHocLog());

        var sodium = Assert.Single(await ProgressAsync(client));

        Assert.Equal(0m, sodium.Consumed);
        Assert.False(sodium.IsMet);
    }

    [Fact]
    public async Task An_account_with_no_targets_has_no_progress()
    {
        using var client = await RegisterOwnerAsync();

        Assert.Empty(await ProgressAsync(client));
    }

    private static async Task LogAsync(HttpClient client, SaveLogEntryRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/log", request);
        response.EnsureSuccessStatusCode();
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient client,
        params (string Key, decimal Target, GoalKind Kind)[] goals) =>
        client.PutAsJsonAsync("/api/goals", new SaveGoalsRequest
        {
            Goals = [.. goals.Select(goal => new SaveGoalRequest
            {
                NutrientKey = goal.Key,
                Target = goal.Target,
                Kind = goal.Kind
            })]
        });

    private static async Task<GoalResponse[]> SaveAsync(
        HttpClient client,
        params (string Key, decimal Target, GoalKind Kind)[] goals)
    {
        using var response = await PutAsync(client, goals);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<GoalResponse[]>())!;
    }

    private static async Task<GoalResponse[]> GetAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<GoalResponse[]>("/api/goals"))!;

    private static async Task<GoalProgressResponse[]> ProgressAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<GoalProgressResponse[]>("/api/goals/progress"))!;
}
