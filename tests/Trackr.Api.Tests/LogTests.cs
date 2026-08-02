using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackr.Api.Data;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The log: snapshots, quantity arithmetic, and which day an entry belongs to.
/// </summary>
public sealed class LogTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    /// <summary>CLAUDE.md section 7's snapshot rule.</summary>
    [Fact]
    public async Task A_logged_item_keeps_its_snapshot_when_the_catalog_item_changes()
    {
        using var client = await RegisterOwnerAsync();

        var food = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(
                energyKcal: 210.5m,
                nutrients: new Dictionary<string, decimal> { ["sodium"] = 120m }));

        using var logged = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(food));
        logged.EnsureSuccessStatusCode();

        using var edited = await client.PutAsJsonAsync(
            $"/api/foods/{food.Id}",
            Payloads.Food(
                energyKcal: 999m,
                nutrients: new Dictionary<string, decimal> { ["sodium"] = 999m }));
        edited.EnsureSuccessStatusCode();

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");
        var item = Assert.Single(Assert.Single(entries!).Items);

        Assert.Equal(210.5m, item.EnergyKcal);
        Assert.Equal(120m, item.Nutrients["sodium"]);
    }

    /// <summary>The test that would have caught a cascade on the food-item foreign key.</summary>
    [Fact]
    public async Task Deleting_a_catalog_item_leaves_the_log_intact()
    {
        using var client = await RegisterOwnerAsync();

        var food = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(nutrients: new Dictionary<string, decimal> { ["sodium"] = 120m }));

        using var logged = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(food));
        logged.EnsureSuccessStatusCode();

        using var deleted = await client.DeleteAsync($"/api/foods/{food.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");
        var item = Assert.Single(Assert.Single(entries!).Items);

        // The row and every number on it survive; only the back-link goes.
        Assert.Null(item.FoodItemId);
        Assert.Equal(food.Name, item.Name);
        Assert.Equal(120m, item.Nutrients["sodium"]);
    }

    [Fact]
    public async Task Quantity_is_multiplied_into_the_snapshot()
    {
        using var client = await RegisterOwnerAsync();

        var food = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(
                energyKcal: 10m,
                nutrients: new Dictionary<string, decimal> { ["sodium"] = 4m }));

        using var response = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(food, quantity: 2.5m));
        response.EnsureSuccessStatusCode();

        var entry = await response.Content.ReadFromJsonAsync<LogEntryResponse>();
        var item = Assert.Single(entry!.Items);

        // Stored as totals, not per-serving values: that is what makes the stats views a SUM.
        Assert.Equal(2.5m, item.Quantity);
        Assert.Equal(25m, item.EnergyKcal);
        Assert.Equal(10m, item.Nutrients["sodium"]);
    }

    /// <summary>
    /// Proves the day boundary and the half-open interval in one.
    /// </summary>
    /// <remarks>
    /// The middle entry is the one that matters: with an inclusive <c>BETWEEN</c> ending at
    /// 23:59:59.999, a meal logged in the last microsecond of the day silently belongs to no day
    /// at all. Postgres keeps microseconds, so this is a real value rather than a contrived one.
    /// </remarks>
    [Fact]
    public async Task The_day_filter_uses_a_half_open_interval()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food());

        var midnight = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var lastMicrosecond = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero).AddTicks(-10);
        var nextMidnight = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

        foreach (var moment in new[] { midnight, lastMicrosecond, nextMidnight })
        {
            using var response = await client.PostAsJsonAsync(
                "/api/log",
                Payloads.LogOf(food, loggedUtc: moment));

            response.EnsureSuccessStatusCode();
        }

        var day = await client.GetFromJsonAsync<LogEntryResponse[]>(
            "/api/log?from=2026-01-01&to=2026-01-01");

        Assert.Equal(2, day!.Length);
        Assert.Equal(midnight, day[0].LoggedUtc);
        Assert.Equal(lastMicrosecond, day[1].LoggedUtc);
    }

    [Fact]
    public async Task A_range_covers_every_day_it_names()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food());

        foreach (var day in new[] { 1, 3, 8 })
        {
            using var response = await client.PostAsJsonAsync(
                "/api/log",
                Payloads.LogOf(food, loggedUtc: new DateTimeOffset(2026, 1, day, 12, 0, 0, TimeSpan.Zero)));

            response.EnsureSuccessStatusCode();
        }

        var week = await client.GetFromJsonAsync<LogEntryResponse[]>(
            "/api/log?from=2026-01-01&to=2026-01-07");

        Assert.Equal(2, week!.Length);
    }

    /// <remarks>
    /// The pair is the point: refusing another account's personal item proves the check exists,
    /// and accepting a global one proves it is a check on visibility rather than on ownership -
    /// which is what the shared catalog needs it to be.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_personal_food_item_cannot_be_linked_but_a_global_one_can()
    {
        using var owner = await RegisterOwnerAsync();
        var personal = await FoodCatalogTests.CreateAsync(owner, Payloads.Food(name: "Owners own"));
        var shared = await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(name: "Household beans", visibility: FoodVisibility.Global));

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var refused = await member.PostAsJsonAsync("/api/log", Payloads.LogOf(personal));
        using var accepted = await member.PostAsJsonAsync("/api/log", Payloads.LogOf(shared));

        // Without this check, one account could read another's private catalog back out of the
        // snapshot fields in the response.
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
    }

    [Fact]
    public async Task An_entry_needs_at_least_one_item()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/log",
            new SaveLogEntryRequest { Note = "nothing in particular" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_accounts_entry_is_not_reachable()
    {
        using var owner = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(owner, Payloads.Food());

        using var created = await owner.PostAsJsonAsync("/api/log", Payloads.LogOf(food));
        var entry = await created.Content.ReadFromJsonAsync<LogEntryResponse>();

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var byId = await member.GetAsync($"/api/log/{entry!.Id}");
        using var deleted = await member.DeleteAsync($"/api/log/{entry.Id}");
        var listed = await member.GetFromJsonAsync<LogEntryResponse[]>("/api/log");

        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
        Assert.Empty(listed!);
    }

    [Fact]
    public async Task Deleting_an_entry_leaves_no_items_or_nutrients_behind()
    {
        using var client = await RegisterOwnerAsync();

        var food = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(nutrients: new Dictionary<string, decimal> { ["sodium"] = 120m }));

        using var created = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(food));
        var entry = await created.Content.ReadFromJsonAsync<LogEntryResponse>();

        using var deleted = await client.DeleteAsync($"/api/log/{entry!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();

        Assert.Empty(await db.LogEntries.ToListAsync());
        Assert.Empty(await db.LogItems.ToListAsync());
        Assert.Empty(await db.LogItemNutrients.ToListAsync());

        // And the catalog item it referenced is untouched - the cascade runs one way only.
        Assert.True(await db.FoodItems.AnyAsync(item => item.Id == food.Id));
    }

    [Fact]
    public async Task An_entry_can_be_replaced_wholesale()
    {
        using var client = await RegisterOwnerAsync();
        var food = await FoodCatalogTests.CreateAsync(client, Payloads.Food(energyKcal: 100m));

        using var created = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(food));
        var entry = await created.Content.ReadFromJsonAsync<LogEntryResponse>();

        var replacement = Payloads.LogOf(food, quantity: 3m);
        replacement.Note = "actually three";

        using var replaced = await client.PutAsJsonAsync($"/api/log/{entry!.Id}", replacement);
        replaced.EnsureSuccessStatusCode();

        var updated = await client.GetFromJsonAsync<LogEntryResponse>($"/api/log/{entry.Id}");
        var item = Assert.Single(updated!.Items);

        Assert.Equal("actually three", updated.Note);
        Assert.Equal(300m, item.EnergyKcal);
    }

    [Fact]
    public async Task An_absurd_range_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        using var backwards = await client.GetAsync("/api/log?from=2026-02-01&to=2026-01-01");
        using var enormous = await client.GetAsync("/api/log?from=2000-01-01&to=2026-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, backwards.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, enormous.StatusCode);
    }
}
