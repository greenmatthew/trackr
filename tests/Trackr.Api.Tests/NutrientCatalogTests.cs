using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackr.Api.Data;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The seeded nutrient reference set.
/// </summary>
/// <remarks>
/// Everything else in this milestone rests on these rows existing: an amount cannot be stored
/// against a key that has no row, so a failed seed would surface as a foreign-key violation on
/// somebody's first save rather than as anything legible.
/// </remarks>
public sealed class NutrientCatalogTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task Every_nutrient_in_the_seed_is_served()
    {
        using var client = await RegisterOwnerAsync();

        var nutrients = await client.GetFromJsonAsync<NutrientResponse[]>("/api/nutrients");

        Assert.Equal(NutrientSeed.All.Count, nutrients!.Length);
        Assert.Equal(29, nutrients.Length);
    }

    [Fact]
    public async Task A_nutrient_carries_its_unit_and_its_group()
    {
        using var client = await RegisterOwnerAsync();

        var nutrients = await client.GetFromJsonAsync<NutrientResponse[]>("/api/nutrients");

        var vitaminC = Assert.Single(nutrients!, nutrient => nutrient.Key == "vitamin_c");

        // The unit is the whole reason this table exists: wiki/Nutrient-Reference.md calls getting
        // it silently wrong worse than not recording the value.
        Assert.Equal(NutrientUnit.Milligram, vitaminC.Unit);
        Assert.Equal(NutrientGroup.Vitamins, vitaminC.Group);
        Assert.Equal("Vitamin C", vitaminC.DisplayName);
    }

    [Fact]
    public async Task Exactly_four_nutrients_are_core()
    {
        using var client = await RegisterOwnerAsync();

        var nutrients = await client.GetFromJsonAsync<NutrientResponse[]>("/api/nutrients");

        var core = nutrients!.Where(nutrient => nutrient.IsCore).Select(nutrient => nutrient.Key);

        Assert.Equal(CoreNutrients.Keys, core);
    }

    [Fact]
    public async Task The_catalog_is_served_in_label_order()
    {
        using var client = await RegisterOwnerAsync();

        var nutrients = await client.GetFromJsonAsync<NutrientResponse[]>("/api/nutrients");

        var orders = nutrients!.Select(nutrient => nutrient.SortOrder).ToArray();

        Assert.Equal(orders.OrderBy(order => order), orders);
        Assert.Equal(orders.Length, orders.Distinct().Count());
    }

    /// <summary>
    /// The property the whole seeding mechanism rests on.
    /// </summary>
    /// <remarks>
    /// The seeder runs on every start, so "starting twice duplicates the catalog" would be a bug
    /// that only ever appears in production. A second factory against the same container is a
    /// second startup: same database, same migration, same seed.
    /// </remarks>
    [Fact]
    public async Task Seeding_twice_does_not_duplicate_or_change_anything()
    {
        using var client = await RegisterOwnerAsync();
        var before = await client.GetFromJsonAsync<NutrientResponse[]>("/api/nutrients");

        await using var second = new TrackrApiFactory(postgres.ConnectionString);
        _ = second.Services;

        using var scope = second.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();

        var after = await db.Nutrients
            .AsNoTracking()
            .OrderBy(nutrient => nutrient.SortOrder)
            .ToListAsync();

        Assert.Equal(NutrientSeed.All.Count, after.Count);
        Assert.Equal(
            before!.Select(nutrient => nutrient.Key),
            after.Select(nutrient => nutrient.Key));
    }
}
