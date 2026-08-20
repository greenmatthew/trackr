using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Milestone 13's load-bearing half: which day a meal belongs to.
/// </summary>
/// <remarks>
/// CLAUDE.md section 9.13 asks for the day boundary to live in exactly one helper so that making it
/// per-user is one change rather than a rewrite of every aggregate. These tests are what say
/// whether that held.
/// </remarks>
public sealed class TimeZoneTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task An_account_starts_on_utc_and_says_so_by_saying_nothing()
    {
        using var client = await RegisterOwnerAsync();

        Assert.Null((await MeAsync(client)).TimeZoneId);
    }

    [Fact]
    public async Task A_zone_is_saved_and_reported_back_on_the_account()
    {
        using var client = await RegisterOwnerAsync();

        Assert.Equal("Europe/London", (await SetZoneAsync(client, "Europe/London")).TimeZoneId);
        Assert.Equal("Europe/London", (await MeAsync(client)).TimeZoneId);
    }

    /// <remarks>
    /// The server's zone database is the only one that matters. A zone the phone knows and the
    /// server does not would silently become UTC, and the day would run on the wrong clock without
    /// anybody being told.
    /// </remarks>
    [Fact]
    public async Task A_zone_this_server_does_not_know_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await PutZoneAsync(client, "Mars/Olympus_Mons");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("timeZoneId", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clearing_a_zone_puts_the_account_back_on_utc()
    {
        using var client = await RegisterOwnerAsync();

        await SetZoneAsync(client, "Europe/London");

        Assert.Null((await SetZoneAsync(client, null)).TimeZoneId);
    }

    /// <summary>
    /// The whole point: the zone decides which local day a meal counts towards.
    /// </summary>
    /// <remarks>
    /// An instant at 23:30 UTC is already the next day in Tokyo, so the same entry lands on
    /// different days depending only on the account's zone - which is what makes storing it per
    /// user rather than sending it per request the thing that matters.
    /// </remarks>
    [Fact]
    public async Task The_zone_decides_which_day_a_meal_counts_towards()
    {
        using var client = await RegisterOwnerAsync();

        // 23:30 UTC on the 19th is 08:30 on the 20th in Tokyo.
        var loggedUtc = new DateTimeOffset(2026, 8, 19, 23, 30, 0, TimeSpan.Zero);

        var request = Payloads.AdHocLog(energyKcal: 500m);
        request.LoggedUtc = loggedUtc;

        using var logged = await client.PostAsJsonAsync("/api/log", request);
        logged.EnsureSuccessStatusCode();

        Assert.Equal(500m, await DayEnergyAsync(client, new DateOnly(2026, 8, 19)));
        Assert.Equal(0m, await DayEnergyAsync(client, new DateOnly(2026, 8, 20)));

        await SetZoneAsync(client, "Asia/Tokyo");

        Assert.Equal(0m, await DayEnergyAsync(client, new DateOnly(2026, 8, 19)));
        Assert.Equal(500m, await DayEnergyAsync(client, new DateOnly(2026, 8, 20)));
    }

    /// <remarks>
    /// Goal progress totals a day too, and it must be the same day the stats views mean.
    /// </remarks>
    [Fact]
    public async Task Goal_progress_uses_the_same_day_the_totals_do()
    {
        using var client = await RegisterOwnerAsync();

        await SetZoneAsync(client, "Asia/Tokyo");

        using var goals = await client.PutAsJsonAsync("/api/goals", new SaveGoalsRequest
        {
            Goals = [new SaveGoalRequest { NutrientKey = CoreNutrients.EnergyKcal, Target = 100m, Kind = GoalKind.AtMost }]
        });
        goals.EnsureSuccessStatusCode();

        var request = Payloads.AdHocLog(energyKcal: 500m);
        request.LoggedUtc = new DateTimeOffset(2026, 8, 19, 23, 30, 0, TimeSpan.Zero);

        using var logged = await client.PostAsJsonAsync("/api/log", request);
        logged.EnsureSuccessStatusCode();

        var onTheTwentieth = await client.GetFromJsonAsync<GoalProgressResponse[]>(
            "/api/goals/progress?date=2026-08-20");

        Assert.Equal(500m, Assert.Single(onTheTwentieth!).Consumed);

        var onTheNineteenth = await client.GetFromJsonAsync<GoalProgressResponse[]>(
            "/api/goals/progress?date=2026-08-19");

        Assert.Equal(0m, Assert.Single(onTheNineteenth!).Consumed);
    }

    /// <remarks>
    /// One account changing zone must not move anybody else's midnight.
    /// </remarks>
    [Fact]
    public async Task A_zone_belongs_to_one_account()
    {
        using var owner = await RegisterOwnerAsync();
        await SetZoneAsync(owner, "Asia/Tokyo");

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        Assert.Null((await MeAsync(member)).TimeZoneId);
    }

    private static async Task<decimal> DayEnergyAsync(HttpClient client, DateOnly day)
    {
        var stats = await client.GetFromJsonAsync<StatsResponse>(
            $"/api/stats?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}");

        return stats!.Total.EnergyKcal;
    }

    private static Task<HttpResponseMessage> PutZoneAsync(HttpClient client, string? id) =>
        client.PutAsJsonAsync("/api/account/timezone", new SaveTimeZoneRequest { TimeZoneId = id });

    private static async Task<MeResponse> SetZoneAsync(HttpClient client, string? id)
    {
        using var response = await PutZoneAsync(client, id);

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<MeResponse>())!;
    }

    private static async Task<MeResponse> MeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!;
}
