using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Milestone 11: the output surface, added up from log snapshots.
/// </summary>
public sealed class StatsTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Todays_totals_add_up_what_was_logged_today()
    {
        using var client = await RegisterOwnerAsync();

        await LogAsync(client, Payloads.AdHocLog(energyKcal: 330m, quantity: 2m));
        await LogAsync(client, Payloads.AdHocLog(name: "Toast", energyKcal: 80m));

        var stats = await StatsAsync(client);

        Assert.Equal(740m, stats.Total.EnergyKcal);
        Assert.Equal(2, stats.Total.Entries);
        Assert.Equal(1, stats.DaysLogged);
    }

    /// <summary>
    /// The core-four columns are always there; a micronutrient is only there if something reported
    /// it.
    /// </summary>
    /// <remarks>
    /// Seeding the map with zeroes would turn a day of food nobody described in detail into a
    /// confident column of noughts, which is the distinction the nutrient reference exists to
    /// protect.
    /// </remarks>
    [Fact]
    public async Task A_nutrient_nobody_reported_is_absent_rather_than_zero()
    {
        using var client = await RegisterOwnerAsync();

        await LogAsync(
            client,
            Payloads.AdHocLog(nutrients: new Dictionary<string, decimal> { ["sodium"] = 65m }));
        await LogAsync(client, Payloads.AdHocLog(name: "Toast"));

        var stats = await StatsAsync(client);

        Assert.Equal(65m, stats.Total.Nutrients["sodium"]);
        Assert.False(stats.Total.Nutrients.ContainsKey("fibre"));
    }

    [Fact]
    public async Task A_day_with_nothing_on_it_is_still_a_day()
    {
        using var client = await RegisterOwnerAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var stats = await StatsAsync(client, today.AddDays(-6), today);

        Assert.Equal(7, stats.Days.Count);
        Assert.All(stats.Days, day => Assert.False(day.HasAnything));
        Assert.Equal(0, stats.DaysLogged);
    }

    /// <summary>
    /// The average divides by the days that have something on them, not by the length of the range.
    /// </summary>
    /// <remarks>
    /// Three days logged out of seven, divided by seven, reports a number nobody ate and makes a
    /// partly-filled week look like a fast.
    /// </remarks>
    [Fact]
    public async Task The_average_is_per_day_logged_rather_than_per_day_in_the_range()
    {
        using var client = await RegisterOwnerAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await LogOnAsync(client, today, energyKcal: 1_000m);
        await LogOnAsync(client, today.AddDays(-1), energyKcal: 2_000m);

        var stats = await StatsAsync(client, today.AddDays(-6), today);

        Assert.Equal(2, stats.DaysLogged);
        Assert.Equal(3_000m, stats.Total.EnergyKcal);
        Assert.Equal(1_500m, stats.AveragePerLoggedDay.EnergyKcal);
    }

    [Fact]
    public async Task Each_day_is_totalled_on_its_own()
    {
        using var client = await RegisterOwnerAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await LogOnAsync(client, today, energyKcal: 1_000m);
        await LogOnAsync(client, today.AddDays(-2), energyKcal: 2_000m);

        var stats = await StatsAsync(client, today.AddDays(-2), today);

        Assert.Equal([2_000m, 0m, 1_000m], stats.Days.Select(day => day.EnergyKcal));
    }

    [Fact]
    public async Task Totals_are_the_callers_own_and_nobody_elses()
    {
        using var owner = await RegisterOwnerAsync();
        await LogAsync(owner, Payloads.AdHocLog(energyKcal: 500m));

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        Assert.Equal(0m, (await StatsAsync(member)).Total.EnergyKcal);
    }

    /// <summary>
    /// A correction to a catalog item must not change what a chart said last week.
    /// </summary>
    /// <remarks>
    /// The reason the snapshot exists, expressed as an aggregate: nothing here joins through
    /// LogItem.FoodItemId for a number.
    /// </remarks>
    [Fact]
    public async Task Editing_a_catalog_item_does_not_change_what_was_already_eaten()
    {
        using var client = await RegisterOwnerAsync();

        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food(energyKcal: 210.5m));

        await LogAsync(client, Payloads.LogOf(food));

        using var edited = await client.PutAsJsonAsync($"/api/foods/{food.Id}", Payloads.Food(energyKcal: 999m));
        edited.EnsureSuccessStatusCode();

        Assert.Equal(210.5m, (await StatsAsync(client)).Total.EnergyKcal);
    }

    [Fact]
    public async Task A_backwards_range_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var response = await client.GetAsync(
            $"/api/stats?from={today:yyyy-MM-dd}&to={today.AddDays(-1):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_range_longer_than_a_year_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        using var response = await client.GetAsync(
            $"/api/stats?from={today.AddDays(-400):yyyy-MM-dd}&to={today:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task LogAsync(HttpClient client, SaveLogEntryRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/log", request);
        response.EnsureSuccessStatusCode();
    }

    private static Task LogOnAsync(HttpClient client, DateOnly day, decimal energyKcal)
    {
        var request = Payloads.AdHocLog(energyKcal: energyKcal);

        // Midday, so a test never depends on which side of midnight it runs.
        request.LoggedUtc = new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);

        return LogAsync(client, request);
    }

    private static async Task<StatsResponse> StatsAsync(
        HttpClient client,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var query = from is null
            ? string.Empty
            : $"?from={from:yyyy-MM-dd}&to={to ?? from:yyyy-MM-dd}";

        return (await client.GetFromJsonAsync<StatsResponse>($"/api/stats{query}"))!;
    }
}
