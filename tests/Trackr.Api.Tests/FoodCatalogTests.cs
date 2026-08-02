using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The catalog, and with it milestone 6's acceptance criterion.
/// </summary>
public sealed class FoodCatalogTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    /// <summary>
    /// CLAUDE.md section 9.6's one sentence, made executable: "confirm you can store and read back
    /// a full multi-nutrient item, not just macros".
    /// </summary>
    /// <remarks>
    /// Every value is <strong>distinct</strong>, which is the point. A fixture that gave every
    /// nutrient the same amount would pass against a mapper that wrote one number everywhere, or
    /// that paired keys to values wrongly - the two bugs most worth catching here.
    /// <para>
    /// One amount is 12.3456, which also makes this the test for the <c>numeric(12,4)</c>
    /// decision: a <c>double precision</c> column fails it.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_food_item_keeps_every_nutrient_it_was_given()
    {
        using var client = await RegisterOwnerAsync();

        var catalog = await client.GetFromJsonAsync<NutrientResponse[]>("/api/nutrients");
        var keys = catalog!.Where(nutrient => !nutrient.IsCore).Select(nutrient => nutrient.Key).ToArray();

        var amounts = keys
            .Select((key, index) => (key, amount: 1.5m + (index * 0.7m)))
            .ToDictionary(pair => pair.key, pair => pair.amount, StringComparer.Ordinal);

        amounts["vitamin_c"] = 12.3456m;

        var created = await CreateAsync(client, Payloads.Food(nutrients: amounts));

        var fetched = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{created.Id}");

        Assert.Equal(25, fetched!.Nutrients.Count);

        foreach (var (key, amount) in amounts)
        {
            Assert.Equal(amount, fetched.Nutrients[key]);
        }

        // The core four are columns, not map entries. A client that summed the map and then added
        // the typed properties would otherwise count them twice.
        Assert.DoesNotContain(fetched.Nutrients.Keys, CoreNutrients.IsCore);
        Assert.Equal(210.5m, fetched.EnergyKcal);
        Assert.Equal(9.25m, fetched.FatG);
        Assert.Equal(22.125m, fetched.CarbohydrateG);
        Assert.Equal(7.5m, fetched.ProteinG);
    }

    /// <summary>Half of "missing is not zero".</summary>
    [Fact]
    public async Task Missing_nutrients_stay_missing()
    {
        using var client = await RegisterOwnerAsync();

        var created = await CreateAsync(
            client,
            Payloads.Food(nutrients: new Dictionary<string, decimal> { ["vitamin_c"] = 30m }));

        var fetched = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{created.Id}");

        Assert.True(fetched!.Nutrients.ContainsKey("vitamin_c"));

        // Absent, not zero. A dashboard that rendered every unmeasured micronutrient as 0 would
        // make a photo estimate look like a nutritional catastrophe.
        Assert.False(fetched.Nutrients.ContainsKey("sodium"));
    }

    /// <summary>The other half.</summary>
    [Fact]
    public async Task A_known_zero_is_stored_as_zero()
    {
        using var client = await RegisterOwnerAsync();

        var created = await CreateAsync(
            client,
            Payloads.Food(nutrients: new Dictionary<string, decimal> { ["trans_fat"] = 0m }));

        var fetched = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{created.Id}");

        Assert.True(fetched!.Nutrients.TryGetValue("trans_fat", out var transFat));
        Assert.Equal(0m, transFat);
    }

    [Fact]
    public async Task An_unknown_nutrient_is_refused_and_named()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(nutrients: new Dictionary<string, decimal> { ["selenium"] = 5m }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Named, because "one of your nutrients is wrong" is not a message anyone can act on.
        Assert.Contains("selenium", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_core_nutrient_cannot_be_sent_in_the_map()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(nutrients: new Dictionary<string, decimal> { ["protein"] = 5m }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("protein", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// A replace rather than a merge, which is what makes "that nutrient was wrong, remove it"
    /// expressible at all.
    /// </remarks>
    [Fact]
    public async Task Replacing_an_item_replaces_its_whole_nutrient_map()
    {
        using var client = await RegisterOwnerAsync();

        var created = await CreateAsync(
            client,
            Payloads.Food(nutrients: new Dictionary<string, decimal>
            {
                ["vitamin_c"] = 30m,
                ["sodium"] = 120m
            }));

        var replacement = Payloads.Food(
            name: "Oat bar, corrected",
            nutrients: new Dictionary<string, decimal> { ["sodium"] = 90m });

        using var response = await client.PutAsJsonAsync($"/api/foods/{created.Id}", replacement);
        response.EnsureSuccessStatusCode();

        var fetched = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{created.Id}");

        Assert.Equal("Oat bar, corrected", fetched!.Name);
        Assert.Equal(90m, fetched.Nutrients["sodium"]);
        Assert.False(fetched.Nutrients.ContainsKey("vitamin_c"));
    }

    [Fact]
    public async Task One_account_cannot_have_two_items_with_the_same_barcode()
    {
        using var client = await RegisterOwnerAsync();

        await CreateAsync(client, Payloads.Food(barcode: "5012345678900"));

        using var second = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(name: "A second scan", barcode: "5012345678900"));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    /// <remarks>
    /// The partial unique index is per account, so two people each keeping their own record of the
    /// same product is fine - it is only the shared catalog that must hold one of each.
    /// </remarks>
    [Fact]
    public async Task Two_accounts_may_each_have_their_own_item_with_one_barcode()
    {
        using var owner = await RegisterOwnerAsync();
        await CreateAsync(owner, Payloads.Food(barcode: "5012345678900"));

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var response = await member.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(barcode: "5012345678900"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task A_barcode_has_to_be_digits()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(barcode: "not-a-barcode"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_serving_of_nothing_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        var request = Payloads.Food();
        request.ServingSize = 0m;

        using var response = await client.PostAsJsonAsync("/api/foods", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_catalog_can_be_searched_by_name_and_brand()
    {
        using var client = await RegisterOwnerAsync();

        await CreateAsync(client, Payloads.Food(name: "Oat bar", brand: "Trackr"));
        await CreateAsync(client, Payloads.Food(name: "Rye bread", brand: "Bakery"));

        var byName = await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods?search=oat");
        var byBrand = await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods?search=bakery");

        Assert.Equal("Oat bar", Assert.Single(byName!).Name);
        Assert.Equal("Rye bread", Assert.Single(byBrand!).Name);
    }

    internal static async Task<FoodItemResponse> CreateAsync(HttpClient client, SaveFoodItemRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/foods", request);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<FoodItemResponse>()
            ?? throw new InvalidOperationException("The catalog endpoint returned no body.");
    }
}
