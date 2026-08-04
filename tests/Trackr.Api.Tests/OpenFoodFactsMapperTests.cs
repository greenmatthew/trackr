using System.Text.Json;
using Trackr.Api.Cascade;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The mapper, against real Open Food Facts responses captured from the live API.
/// </summary>
/// <remarks>
/// No database and no network: the mapper is pure, which is the reason it is a separate class from
/// the client. Every expected number below was worked out from the fixture by hand, so a test
/// failing here means the mapper changed its mind about what OFF's data means.
/// <para>
/// The fixtures are three real products chosen because each breaks a different assumption:
/// <list type="bullet">
/// <item><c>nutella</c> - per-100 g figures and <strong>no serving size at all</strong>.</item>
/// <item><c>pringles</c> - per-serving figures, a 28 g serving, iron declared in milligrams on a US
/// label, and a second <c>carbohydrates-total</c> key that disagrees with <c>carbohydrates</c>.</item>
/// <item><c>diet-coke</c> - measured per <strong>100 ml</strong> rather than 100 g, with a 354.9 ml
/// serving and genuine zeroes throughout.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class OpenFoodFactsMapperTests
{
    private readonly NutrientCatalog _catalog = new();

    [Fact]
    public void Every_nutrient_the_server_tracks_has_an_open_food_facts_name()
    {
        var unmapped = NutrientSeed.All
            .Where(nutrient => !nutrient.IsCore)
            .Select(nutrient => nutrient.Key)
            .Where(key => !OpenFoodFactsNutrients.OffStemsByNutrientKey.ContainsKey(key))
            .ToArray();

        // Adding a nutrient to NutrientSeed without recording what Open Food Facts calls it would
        // silently stop mapping it - the value would simply never appear, with nothing to notice.
        Assert.Empty(unmapped);
    }

    [Fact]
    public void The_map_never_claims_a_nutrient_the_server_does_not_have()
    {
        var unknown = OpenFoodFactsNutrients.OffStemsByNutrientKey.Keys
            .Where(key => !_catalog.Contains(key))
            .ToArray();

        Assert.Empty(unknown);
    }

    /// <remarks>
    /// The plain case, and the one that proves the headline conversion: OFF reports per 100 g, the
    /// product has no serving size, so one serving is taken to be 100 g and the figures pass through
    /// unscaled.
    /// </remarks>
    [Fact]
    public void A_product_with_no_serving_size_is_mapped_per_100_g()
    {
        var result = Map("nutella");

        Assert.Equal(ProductLookupOutcome.Matched, result.Outcome);

        var product = result.Product!;

        Assert.Equal("Nutella", product.Name);
        Assert.Equal("3017620422003", product.Barcode);
        Assert.Equal(ServingBasis.ReferenceQuantityAsServing, product.ServingBasis);
        Assert.Equal(100m, product.ServingSize);
        Assert.Equal("g", product.ServingUnit);

        Assert.Equal(539m, product.EnergyKcal);
        Assert.Equal(30.9m, product.FatG);
        Assert.Equal(57.5m, product.CarbohydrateG);
        Assert.Equal(6.3m, product.ProteinG);

        Assert.Equal(10.6m, product.Nutrients["saturated_fat"]);
        Assert.Equal(56.3m, product.Nutrients["sugars"]);
        Assert.Equal(52.13m, product.Nutrients["added_sugars"]);

        // 0.0428 g of sodium, recorded in milligrams because that is the unit its catalog row
        // carries. This is the whole reason PerGram exists.
        Assert.Equal(42.8m, product.Nutrients["sodium"]);
    }

    /// <remarks>
    /// The assumed serving size is the sort of thing a user would otherwise discover by being
    /// surprised at a total, so section 5 requires it to be said out loud.
    /// </remarks>
    [Fact]
    public void An_assumed_serving_size_is_warned_about()
    {
        var result = Map("nutella");

        Assert.Contains(result.Warnings, warning => warning.Contains("no serving size"));
        Assert.Contains(result.Warnings, warning => warning.Contains("100 g"));
    }

    /// <remarks>
    /// Per-serving figures are preferred over per-100 g ones, so nothing has to be scaled and the
    /// numbers are the ones printed on the packet.
    /// </remarks>
    [Fact]
    public void A_product_with_a_serving_size_is_mapped_per_serving()
    {
        var result = Map("pringles");

        Assert.Equal(ProductLookupOutcome.Matched, result.Outcome);

        var product = result.Product!;

        Assert.Equal("Original Potato Crisps", product.Name);
        Assert.Equal("Pringles", product.Brand);
        Assert.Equal(ServingBasis.LabelServing, product.ServingBasis);
        Assert.Equal(28m, product.ServingSize);
        Assert.Equal("g", product.ServingUnit);

        // The label's own per-serving numbers, not 28% of the per-100 g ones.
        Assert.Equal(150m, product.EnergyKcal);
        Assert.Equal(8.68m, product.FatG);
        Assert.Equal(1.74m, product.ProteinG);

        // carbohydrates (14 g per serving), not carbohydrates-total (16 g). Both are present in the
        // fixture and they disagree, which is exactly why the preference order is written down.
        Assert.Equal(14m, product.CarbohydrateG);

        // 0.0003 g of iron per serving. The label says 0.3 mg; OFF normalises it to grams whatever
        // the label used, and reading its _value field instead would land a thousand times out.
        Assert.Equal(0.3m, product.Nutrients["iron"]);

        Assert.Equal(112m, product.Nutrients["sodium"]);
        Assert.Empty(result.Warnings);
    }

    /// <remarks>
    /// A drink, and the reason the reference unit is read from <c>nutrition_data_per</c> rather than
    /// assumed: OFF still names the keys <c>_100g</c> for something measured per 100 ml.
    /// </remarks>
    [Fact]
    public void A_drink_keeps_millilitres_as_its_serving_unit()
    {
        var result = Map("diet-coke");

        Assert.Equal(ProductLookupOutcome.Matched, result.Outcome);

        var product = result.Product!;

        Assert.Equal(354.9m, product.ServingSize);
        Assert.Equal("ml", product.ServingUnit);
        Assert.Equal(ServingBasis.LabelServing, product.ServingBasis);
    }

    /// <remarks>
    /// The distinction CLAUDE.md section 7 insists on, in the one place it is easiest to lose: a
    /// diet drink really does have zero fat, and mapping that to "not measured" would be as wrong as
    /// mapping an unmeasured value to zero.
    /// </remarks>
    [Fact]
    public void A_declared_zero_is_kept_as_zero_rather_than_dropped()
    {
        var product = Map("diet-coke").Product!;

        Assert.Equal(0m, product.EnergyKcal);
        Assert.Equal(0m, product.FatG);
        Assert.Equal(0m, product.CarbohydrateG);
        Assert.Equal(0m, product.ProteinG);

        // 0.0401 g per serving, in milligrams. A real measurement that happens to be small.
        Assert.Equal(40.1m, product.Nutrients["sodium"]);
    }

    /// <remarks>
    /// The core four are columns rather than map entries, so a client that summed the map and then
    /// added the typed fields would double count. Enforced here as well as by a database CHECK
    /// constraint, because this mapper is a second author of the map.
    /// </remarks>
    [Theory]
    [InlineData("nutella")]
    [InlineData("pringles")]
    [InlineData("diet-coke")]
    public void The_nutrient_map_never_contains_a_core_nutrient(string fixture)
    {
        var product = Map(fixture).Product!;

        Assert.DoesNotContain(product.Nutrients.Keys, CoreNutrients.IsCore);
    }

    [Theory]
    [InlineData("nutella")]
    [InlineData("pringles")]
    [InlineData("diet-coke")]
    public void Every_mapped_nutrient_is_one_the_server_knows(string fixture)
    {
        var product = Map(fixture).Product!;

        Assert.All(product.Nutrients.Keys, key => Assert.True(_catalog.Contains(key), key));
    }

    /// <remarks>
    /// A partial match is the branch that still sends the photo to the model, so the missing fields
    /// are named rather than merely counted - they become the list the prompt asks it to determine.
    /// </remarks>
    [Fact]
    public void A_product_missing_macros_is_a_partial_match()
    {
        var result = Map(Product("""
            {
                "code": "1234567890123",
                "product_name": "Mystery jar",
                "nutrition_data_per": "100g",
                "nutriments": { "energy-kcal_100g": 250, "fat_100g": 10 }
            }
            """));

        Assert.Equal(ProductLookupOutcome.Partial, result.Outcome);
        Assert.Equal(["carbohydrate", "protein"], result.MissingCoreFields);

        // What was found is still reported: section 5 sends the partial data to the model alongside
        // the image rather than throwing it away and starting from the photo alone.
        Assert.Equal(250m, result.Product!.EnergyKcal);
        Assert.Equal(10m, result.Product.FatG);
        Assert.Null(result.Product.ProteinG);
    }

    [Fact]
    public void A_product_with_no_name_is_a_partial_match()
    {
        var result = Map(Product("""
            {
                "code": "1234567890123",
                "nutriments": {
                    "energy-kcal_100g": 250, "fat_100g": 10,
                    "carbohydrates_100g": 30, "proteins_100g": 5
                }
            }
            """));

        Assert.Equal(ProductLookupOutcome.Partial, result.Outcome);
        Assert.Equal(["name"], result.MissingCoreFields);
    }

    /// <remarks>
    /// OFF is crowd-edited and two decades old: a number arriving as a quoted string is ordinary,
    /// and losing a whole product over one field would not be.
    /// </remarks>
    [Fact]
    public void Numbers_sent_as_strings_are_still_read()
    {
        var result = Map(Product("""
            {
                "code": "1234567890123",
                "product_name": "Stringly typed",
                "serving_quantity": "30",
                "nutriments": {
                    "energy-kcal_100g": "250", "fat_100g": "10",
                    "carbohydrates_100g": "30", "proteins_100g": "5"
                }
            }
            """));

        Assert.Equal(ProductLookupOutcome.Matched, result.Outcome);

        var product = result.Product!;

        Assert.Equal(30m, product.ServingSize);
        Assert.Equal(ServingBasis.ScaledFromReferenceQuantity, product.ServingBasis);

        // 30 g of a 250 kcal/100 g product.
        Assert.Equal(75m, product.EnergyKcal);
        Assert.Equal(3m, product.FatG);
    }

    /// <remarks>
    /// Nonsense is treated as "not measured" rather than guessed at, because a null shows the user
    /// a gap while a wrong number shows them a fact.
    /// </remarks>
    [Fact]
    public void Unusable_values_are_treated_as_not_measured()
    {
        var result = Map(Product("""
            {
                "code": "1234567890123",
                "product_name": "Bad data",
                "nutriments": {
                    "energy-kcal_100g": 250, "fat_100g": -5,
                    "carbohydrates_100g": "not a number", "proteins_100g": null,
                    "sodium_100g": -1
                }
            }
            """));

        Assert.Equal(ProductLookupOutcome.Partial, result.Outcome);
        Assert.Equal(["fat", "carbohydrate", "protein"], result.MissingCoreFields);
        Assert.DoesNotContain("sodium", result.Product!.Nutrients.Keys);
    }

    /// <remarks>
    /// A serving size of zero would make every per-serving figure zero by multiplication, so it is
    /// refused and the per-100 basis used instead.
    /// </remarks>
    [Fact]
    public void A_zero_serving_size_falls_back_to_the_reference_quantity()
    {
        var result = Map(Product("""
            {
                "code": "1234567890123",
                "product_name": "Zero serving",
                "serving_quantity": 0,
                "nutriments": {
                    "energy-kcal_100g": 250, "fat_100g": 10,
                    "carbohydrates_100g": 30, "proteins_100g": 5
                }
            }
            """));

        var product = result.Product!;

        Assert.Equal(ServingBasis.ReferenceQuantityAsServing, product.ServingBasis);
        Assert.Equal(100m, product.ServingSize);
        Assert.Equal(250m, product.EnergyKcal);
    }

    /// <remarks>
    /// A per-serving product whose micronutrients OFF only stored per 100 g would otherwise lose
    /// them entirely, because the basis is chosen once for the whole product.
    /// </remarks>
    [Fact]
    public void A_nutrient_stored_under_the_other_basis_is_converted_rather_than_dropped()
    {
        var result = Map(Product("""
            {
                "code": "1234567890123",
                "product_name": "Mixed bases",
                "serving_quantity": 50,
                "nutriments": {
                    "energy-kcal_serving": 125, "fat_serving": 5,
                    "carbohydrates_serving": 15, "proteins_serving": 2.5,
                    "calcium_100g": 0.2
                }
            }
            """));

        var product = result.Product!;

        Assert.Equal(ServingBasis.LabelServing, product.ServingBasis);

        // 0.2 g per 100 g is 0.1 g per 50 g serving, which is 100 mg.
        Assert.Equal(100m, product.Nutrients["calcium"]);
    }

    /// <remarks>
    /// OFF attaches <c>nova-group_serving</c> - a processing score, not a measurement - to products
    /// with no per-serving nutrition at all. Treating any <c>_serving</c> key as evidence of
    /// per-serving figures would pick that basis and then read nothing but nulls out of it.
    /// </remarks>
    [Fact]
    public void A_processing_score_is_not_mistaken_for_per_serving_nutrition()
    {
        var result = Map(Product("""
            {
                "code": "1234567890123",
                "product_name": "Scored but not served",
                "nutriments": {
                    "nova-group_serving": 4,
                    "energy-kcal_100g": 250, "fat_100g": 10,
                    "carbohydrates_100g": 30, "proteins_100g": 5
                }
            }
            """));

        Assert.Equal(ProductLookupOutcome.Matched, result.Outcome);
        Assert.Equal(ServingBasis.ReferenceQuantityAsServing, result.Product!.ServingBasis);
        Assert.Equal(250m, result.Product.EnergyKcal);
    }

    [Fact]
    public void Only_the_first_of_open_food_facts_brand_list_is_kept()
    {
        // "Nutella, Ferrero, Yum yum" in the fixture: a list, not one brand.
        Assert.Equal("Nutella", Map("nutella").Product!.Brand);
    }

    private ProductLookupResult Map(string fixture) => Map(LoadFixture(fixture));

    private ProductLookupResult Map(OpenFoodFactsProduct product) =>
        OpenFoodFactsMapper.Map(product.Code ?? "0000000000000", product, _catalog);

    /// <summary>Parses a product out of a whole captured API response.</summary>
    private static OpenFoodFactsProduct LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OpenFoodFacts", $"{name}.json");

        var response = JsonSerializer.Deserialize<OpenFoodFactsResponse>(File.ReadAllText(path));

        Assert.NotNull(response?.Product);

        return response.Product;
    }

    /// <summary>Parses a hand-written product body, for the cases no real product illustrates.</summary>
    private static OpenFoodFactsProduct Product(string json) =>
        JsonSerializer.Deserialize<OpenFoodFactsProduct>(json)!;
}
