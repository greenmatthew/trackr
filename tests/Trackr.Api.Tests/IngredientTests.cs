using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Milestone 10a: what a product is made of, and which products may say.
/// </summary>
/// <remarks>
/// The rule doing the work here is that an ingredient list is only true of one brand's
/// formulation. It is why this is not the nutrient store wearing a different hat, and why most of
/// these tests are about refusing one.
/// </remarks>
public sealed class IngredientTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task A_branded_item_keeps_what_it_is_made_of()
    {
        using var client = await RegisterOwnerAsync();

        var item = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(
                ingredientsText: "Oats, sugar, palm oil, salt.",
                allergens: ["en:gluten"],
                dietFlags: ["en:palm-oil", "en:vegan"]));

        var read = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{item.Id}");

        Assert.Equal("Oats, sugar, palm oil, salt.", read!.IngredientsText);
        Assert.Equal(["en:gluten"], read.Allergens);
        Assert.Equal(["en:palm-oil", "en:vegan"], read.DietFlags);
    }

    /// <summary>
    /// The constraint CLAUDE.md section 7 sets before the schema is written.
    /// </summary>
    /// <remarks>
    /// "Chicken breast" has no formulation to describe, and the catalog is shared and editable by
    /// any account - so one person's guess at what a generic food contains would become everyone's.
    /// </remarks>
    [Fact]
    public async Task A_generic_item_may_not_claim_an_ingredient_list()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(
                name: "Chicken breast",
                brand: null,
                barcode: null,
                ingredientsText: "Chicken."));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ingredientsText", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_barcode_is_enough_to_carry_one_without_a_brand()
    {
        using var client = await RegisterOwnerAsync();

        var item = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(brand: null, barcode: "0076840100446", ingredientsText: "Cream, sugar."));

        var read = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{item.Id}");

        Assert.Equal("Cream, sugar.", read!.IngredientsText);
    }

    /// <remarks>
    /// A recipe derives its ingredients from the items it is made of, the same way it derives its
    /// nutrition. A stored list would be a second version of the same fact, free to disagree.
    /// </remarks>
    [Fact]
    public async Task A_recipe_may_not_be_given_a_list_of_its_own()
    {
        using var client = await RegisterOwnerAsync();

        var flour = await FoodCatalogTests.CreateAsync(client, Payloads.Food(name: "Flour"));

        var recipe = Payloads.Recipe(yield: 4m, components: (flour, 2m));
        recipe.IngredientsText = "Flour and hope.";

        using var response = await client.PostAsJsonAsync("/api/foods", recipe);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ingredientsText", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// These are Open Food Facts identifiers rather than prose, so two spellings differing only in
    /// case would be two allergens as far as a query is concerned.
    /// </remarks>
    [Fact]
    public async Task Allergen_tags_are_lowercased_and_de_duplicated()
    {
        using var client = await RegisterOwnerAsync();

        var item = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(allergens: ["EN:Milk", " en:milk ", "en:nuts"]));

        var read = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{item.Id}");

        Assert.Equal(["en:milk", "en:nuts"], read!.Allergens);
    }

    /// <remarks>
    /// A list is what a question like "which of these contain nuts" is asked of, and five short
    /// strings are nothing beside the nutrient map a list deliberately leaves behind.
    /// </remarks>
    [Fact]
    public async Task Allergens_travel_with_the_catalog_list()
    {
        using var client = await RegisterOwnerAsync();

        await FoodCatalogTests.CreateAsync(client, Payloads.Food(allergens: ["en:milk"]));

        var listed = await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods");

        Assert.Equal(["en:milk"], Assert.Single(listed!).Allergens);
    }

    /// <remarks>
    /// Wholesale, like the nutrient map: a merge leaves "that list was wrong, here is the right
    /// one" inexpressible.
    /// </remarks>
    [Fact]
    public async Task Editing_an_item_replaces_its_ingredients_rather_than_merging_them()
    {
        using var client = await RegisterOwnerAsync();

        var item = await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(ingredientsText: "Oats, palm oil.", allergens: ["en:gluten"]));

        using var edited = await client.PutAsJsonAsync(
            $"/api/foods/{item.Id}",
            Payloads.Food(ingredientsText: "Oats.", allergens: []));
        edited.EnsureSuccessStatusCode();

        var read = await client.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{item.Id}");

        Assert.Equal("Oats.", read!.IngredientsText);
        Assert.Empty(read.Allergens);
    }
}
