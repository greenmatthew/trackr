using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Composite items: recipes made of other catalog items (milestone 7a).
/// </summary>
/// <remarks>
/// Two properties carry most of the milestone. Nutrition is <strong>materialised on write</strong>,
/// so a recipe's own columns always hold the answer and nothing downstream learns composites exist;
/// and because those columns are a cache of the ingredients', correcting an ingredient has to push
/// the new numbers up through everything made of it, across accounts.
/// </remarks>
public sealed class CompositeRecipeTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    /// <summary>
    /// The milestone's acceptance criterion: sum the ingredients, divide by the yield.
    /// </summary>
    /// <remarks>
    /// The two ingredients differ in every value and the quantities differ from each other, so a
    /// mapper that paired quantities to the wrong ingredient - or forgot one - fails rather than
    /// coincidentally agreeing.
    /// </remarks>
    [Fact]
    public async Task A_recipe_gets_its_nutrition_from_its_ingredients()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var recipe = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(yield: 4m, components: [(beans, 2m), (rice, 3m)]));

        // (2 x 100 + 3 x 50) / 4
        Assert.Equal(87.5m, recipe.EnergyKcal);
        Assert.Equal(0.875m, recipe.FatG);
        Assert.Equal(7m, recipe.CarbohydrateG);
        Assert.Equal(2.25m, recipe.ProteinG);
        Assert.Equal(4m, recipe.Yield);

        // Both ingredients report fibre, so the recipe can.
        Assert.Equal(3.5m, recipe.Nutrients["fibre"]);

        Assert.Equal(2, recipe.Components.Count);
        Assert.Contains(recipe.Components, part => part.FoodItemId == beans.Id && part.Quantity == 2m);
    }

    /// <summary>
    /// "Missing is not zero", carried through the arithmetic.
    /// </summary>
    /// <remarks>
    /// Only the beans report sodium. Summing the rice's silence as zero is what every other recipe
    /// tracker does, and it would put a confident understated number on the confirmation card - the
    /// exact failure CLAUDE.md section 2 is written against. Absent is the honest answer.
    /// </remarks>
    [Fact]
    public async Task A_nutrient_one_ingredient_is_silent_about_is_left_out()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var recipe = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(components: [(beans, 2m), (rice, 3m)]));

        Assert.True(beans.Nutrients.ContainsKey("sodium"));
        Assert.False(rice.Nutrients.ContainsKey("sodium"));
        Assert.False(recipe.Nutrients.ContainsKey("sodium"));
    }

    /// <remarks>
    /// A client that fetched an item, renamed it and sent the whole thing back would otherwise have
    /// to strip the computed values out first.
    /// </remarks>
    [Fact]
    public async Task A_recipes_own_numbers_are_ignored()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var request = Payloads.Recipe(components: [(beans, 2m), (rice, 3m)]);
        request.EnergyKcal = 9999m;
        request.Nutrients["vitamin_c"] = 500m;

        var recipe = await FoodCatalogTests.CreateAsync(client, request);

        Assert.Equal(87.5m, recipe.EnergyKcal);
        Assert.False(recipe.Nutrients.ContainsKey("vitamin_c"));
    }

    /// <summary>The fan-out, through two levels of nesting.</summary>
    [Fact]
    public async Task Correcting_an_ingredient_recomputes_every_recipe_above_it()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var bowl = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(name: "Chilli", yield: 4m, components: [(beans, 2m), (rice, 3m)]));

        var meal = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(name: "Chilli dinner", yield: 1m, components: [(bowl, 2m)]));

        Assert.Equal(175m, meal.EnergyKcal);

        // The beans were wrong: 200 kcal a serving, not 100.
        using var correction = await client.PutAsJsonAsync(
            $"/api/foods/{beans.Id}",
            Payloads.Food(
                name: "Beans",
                brand: null,
                energyKcal: 200m,
                fatG: 1m,
                carbohydrateG: 2m,
                proteinG: 3m,
                nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["fibre"] = 4m,
                    ["sodium"] = 10m
                }));

        correction.EnsureSuccessStatusCode();

        // (2 x 200 + 3 x 50) / 4
        var updatedBowl = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{bowl.Id}");
        Assert.Equal(137.5m, updatedBowl!.EnergyKcal);

        // ... and the level above it, which never mentions the beans at all.
        var updatedMeal = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{meal.Id}");
        Assert.Equal(275m, updatedMeal!.EnergyKcal);
    }

    /// <summary>
    /// The reason the recompute ignores who is asking.
    /// </summary>
    /// <remarks>
    /// A global ingredient may be in recipes belonging to several accounts. Recomputing only the
    /// editor's would leave somebody else's dinner quietly reporting the old figure - and they would
    /// have no way of knowing.
    /// </remarks>
    [Fact]
    public async Task Correcting_a_shared_ingredient_recomputes_another_accounts_recipe()
    {
        using var owner = await RegisterOwnerAsync();

        var stock = await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(
                name: "Stock cube",
                brand: null,
                visibility: FoodVisibility.Global,
                energyKcal: 20m,
                fatG: 1m,
                carbohydrateG: 2m,
                proteinG: 1m));

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var soup = await FoodCatalogTests.CreateAsync(
            member,
            Payloads.Recipe(name: "Soup", yield: 2m, components: [(stock, 4m)]));

        Assert.Equal(40m, soup.EnergyKcal);

        using var correction = await owner.PutAsJsonAsync(
            $"/api/foods/{stock.Id}",
            Payloads.Food(
                name: "Stock cube",
                brand: null,
                visibility: FoodVisibility.Global,
                energyKcal: 30m,
                fatG: 1m,
                carbohydrateG: 2m,
                proteinG: 1m));

        correction.EnsureSuccessStatusCode();

        var updated = await member.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{soup.Id}");

        Assert.Equal(60m, updated!.EnergyKcal);
    }

    /// <summary>
    /// The snapshot rule, met from the other side.
    /// </summary>
    /// <remarks>
    /// Materialising on write is what lets the log stay ignorant of composites: a recipe hands the
    /// log per-serving numbers exactly as a scanned package does, and correcting an ingredient
    /// afterwards changes nothing already eaten.
    /// </remarks>
    [Fact]
    public async Task Logging_a_recipe_stores_the_numbers_that_were_confirmed()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var recipe = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(yield: 4m, components: [(beans, 2m), (rice, 3m)]));

        using var logged = await client.PostAsJsonAsync("/api/log", Payloads.LogOf(recipe, quantity: 2m));
        logged.EnsureSuccessStatusCode();

        var entry = await logged.Content.ReadFromJsonAsync<LogEntryResponse>();
        var item = Assert.Single(entry!.Items);

        // Two bowls: the totals, not the per-serving values.
        Assert.Equal(175m, item.EnergyKcal);
        Assert.Equal(7m, item.Nutrients["fibre"]);

        using var correction = await client.PutAsJsonAsync(
            $"/api/foods/{beans.Id}",
            Payloads.Food(name: "Beans", brand: null, energyKcal: 999m));

        correction.EnsureSuccessStatusCode();

        var afterwards = await client.GetFromJsonAsync<LogEntryResponse>($"/api/log/{entry.Id}");

        Assert.Equal(175m, Assert.Single(afterwards!.Items).EnergyKcal);
    }

    [Fact]
    public async Task A_recipe_cannot_contain_itself()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var recipe = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(components: [(beans, 2m), (rice, 3m)]));

        var request = Payloads.Recipe(components: [(beans, 2m)]);
        request.Components.Add(new SaveFoodComponentRequest { FoodItemId = recipe.Id, Quantity = 1m });

        using var response = await client.PutAsJsonAsync($"/api/foods/{recipe.Id}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The case a self-reference check alone would miss, and the one that would loop forever.
    /// </summary>
    [Fact]
    public async Task A_recipe_cannot_contain_itself_through_another_recipe()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var sauce = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(name: "Sauce", components: [(beans, 1m), (rice, 1m)]));

        var dish = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(name: "Dish", components: [(sauce, 2m)]));

        // The sauce is now made of the dish it is an ingredient of.
        using var response = await client.PutAsJsonAsync(
            $"/api/foods/{sauce.Id}",
            Payloads.Recipe(name: "Sauce", components: [(dish, 1m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ingredient of itself", body, StringComparison.Ordinal);
    }

    /// <remarks>
    /// Refused rather than cascaded. The database would drop the ingredient happily and leave the
    /// recipe reporting numbers it can no longer justify.
    /// </remarks>
    [Fact]
    public async Task An_ingredient_cannot_be_deleted_while_a_recipe_uses_it()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(name: "Chilli", components: [(beans, 2m), (rice, 3m)]));

        using var response = await client.DeleteAsync($"/api/foods/{beans.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Named, so the refusal is one somebody can act on.
        Assert.Contains("Chilli", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// Otherwise the household sees a recipe whose ingredient list it cannot open, and the private
    /// item underneath it takes the recipe's numbers with it the day its owner leaves.
    /// </remarks>
    [Fact]
    public async Task A_recipe_cannot_be_shared_while_an_ingredient_is_personal()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var recipe = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(components: [(beans, 2m), (rice, 3m)]));

        using var response = await client.PostAsync($"/api/foods/{recipe.Id}/share", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Beans", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shared_recipe_cannot_be_created_from_personal_ingredients()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Recipe(
                visibility: FoodVisibility.Global,
                components: [(beans, 2m), (rice, 3m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Another_accounts_item_cannot_be_an_ingredient()
    {
        using var owner = await RegisterOwnerAsync();
        var (beans, _) = await IngredientsAsync(owner);

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var response = await member.PostAsJsonAsync(
            "/api/foods",
            Payloads.Recipe(components: [(beans, 1m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_recipe_needs_a_yield()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, _) = await IngredientsAsync(client);

        var request = Payloads.Recipe(components: [(beans, 1m)]);
        request.Yield = null;

        using var response = await client.PostAsJsonAsync("/api/foods", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_yield_without_ingredients_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        var request = Payloads.Food();
        request.Yield = 4m;

        using var response = await client.PostAsJsonAsync("/api/foods", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_ingredient_listed_twice_is_refused()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, _) = await IngredientsAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Recipe(components: [(beans, 1m), (beans, 2m)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <remarks>
    /// A recipe stops being one by being replaced without ingredients, which is what makes "this was
    /// never really a recipe" a correction rather than a delete.
    /// </remarks>
    [Fact]
    public async Task A_recipe_can_become_an_ordinary_item_again()
    {
        using var client = await RegisterOwnerAsync();

        var (beans, rice) = await IngredientsAsync(client);

        var recipe = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Recipe(components: [(beans, 2m), (rice, 3m)]));

        using var response = await client.PutAsJsonAsync(
            $"/api/foods/{recipe.Id}",
            Payloads.Food(name: "Chilli, from a tin", energyKcal: 300m));

        response.EnsureSuccessStatusCode();

        var replaced = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{recipe.Id}");

        Assert.Null(replaced!.Yield);
        Assert.Empty(replaced.Components);
        Assert.Equal(300m, replaced.EnergyKcal);

        // ... and the ingredient is deletable again.
        using var deleted = await client.DeleteAsync($"/api/foods/{beans.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    /// <summary>
    /// Two ingredients whose every value differs, so wrong pairings show up as wrong numbers.
    /// </summary>
    /// <remarks>
    /// The beans report sodium and the rice does not, which is what makes the "missing is not zero"
    /// test above possible without a fixture of its own.
    /// </remarks>
    private static async Task<(FoodItemResponse Beans, FoodItemResponse Rice)> IngredientsAsync(
        HttpClient client)
    {
        var beans = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(
                name: "Beans",
                brand: null,
                energyKcal: 100m,
                fatG: 1m,
                carbohydrateG: 2m,
                proteinG: 3m,
                nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["fibre"] = 4m,
                    ["sodium"] = 10m
                }));

        var rice = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(
                name: "Rice",
                brand: null,
                energyKcal: 50m,
                fatG: 0.5m,
                carbohydrateG: 8m,
                proteinG: 1m,
                nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["fibre"] = 2m
                }));

        return (beans, rice);
    }
}
