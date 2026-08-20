using Trackr.Shared.Nutrition;

namespace Trackr.Api.Tests.Infrastructure;

/// <summary>
/// Request bodies the nutrition tests build on, so each test only writes the part it is about.
/// </summary>
internal static class Payloads
{
    /// <summary>A valid catalog item, with whatever nutrient map the caller cares about.</summary>
    public static SaveFoodItemRequest Food(
        string name = "Oat bar",
        string? brand = "Trackr",
        string? barcode = null,
        FoodVisibility visibility = FoodVisibility.Personal,
        decimal energyKcal = 210.5m,
        decimal fatG = 9.25m,
        decimal carbohydrateG = 22.125m,
        decimal proteinG = 7.5m,
        Dictionary<string, decimal>? nutrients = null) =>
        new()
        {
            Name = name,
            Brand = brand,
            Barcode = barcode,
            ServingSize = 45m,
            ServingUnit = "g",
            Source = FoodSource.Manual,
            Visibility = visibility,
            EnergyKcal = energyKcal,
            FatG = fatG,
            CarbohydrateG = carbohydrateG,
            ProteinG = proteinG,
            Nutrients = nutrients ?? new Dictionary<string, decimal>(StringComparer.Ordinal)
        };

    /// <summary>
    /// A composite item: <paramref name="yield"/> servings' worth, made of the given ingredients.
    /// </summary>
    /// <remarks>
    /// No nutrient values, because a recipe's are computed. Anything sent in those fields is ignored
    /// by the server, which is the behaviour <c>A_recipes_own_numbers_are_ignored</c> pins down.
    /// </remarks>
    public static SaveFoodItemRequest Recipe(
        string name = "Household chilli",
        decimal yield = 4m,
        FoodVisibility visibility = FoodVisibility.Personal,
        params (FoodItemResponse Ingredient, decimal Quantity)[] components) =>
        new()
        {
            Name = name,
            Brand = null,
            ServingSize = 1m,
            ServingUnit = "bowl",
            Source = FoodSource.Manual,
            Visibility = visibility,
            Yield = yield,
            Components =
            [
                .. components.Select(part => new SaveFoodComponentRequest
                {
                    FoodItemId = part.Ingredient.Id,
                    Quantity = part.Quantity
                })
            ]
        };

    /// <summary>An entry logging <paramref name="quantity"/> servings of a catalog item.</summary>
    /// <remarks>
    /// Nutrient values are per serving, as the API expects; the server multiplies them in.
    /// </remarks>
    /// <summary>
    /// A log entry with nothing in the catalog behind it - what a confirmed analysis produces.
    /// </summary>
    /// <remarks>
    /// The barcode is the interesting parameter: it is what milestone 10 keys the catalog on, and
    /// leaving it null is how a test says "the model guessed this one".
    /// </remarks>
    public static SaveLogEntryRequest AdHocLog(
        string name = "Chocolate Therapy",
        string? brand = "Ben & Jerry's",
        string? barcode = null,
        decimal quantity = 1m,
        decimal energyKcal = 330m,
        decimal fatG = 18m,
        decimal carbohydrateG = 36m,
        decimal proteinG = 6m,
        decimal? servingSize = 100m,
        string? servingUnit = "g",
        Dictionary<string, decimal>? nutrients = null) =>
        new()
        {
            Items =
            [
                new SaveLogItemRequest
                {
                    Name = name,
                    Brand = brand,
                    Barcode = barcode,
                    Quantity = quantity,
                    ServingSize = servingSize,
                    ServingUnit = servingUnit,
                    EnergyKcal = energyKcal,
                    FatG = fatG,
                    CarbohydrateG = carbohydrateG,
                    ProteinG = proteinG,
                    Nutrients = nutrients ?? new Dictionary<string, decimal>(StringComparer.Ordinal)
                }
            ]
        };

    public static SaveLogEntryRequest LogOf(
        FoodItemResponse food,
        decimal quantity = 1m,
        DateTimeOffset? loggedUtc = null,
        params Guid[] imageIds) =>
        new()
        {
            LoggedUtc = loggedUtc,
            Items =
            [
                new SaveLogItemRequest
                {
                    FoodItemId = food.Id,
                    Name = food.Name,
                    Brand = food.Brand,
                    Quantity = quantity,
                    ServingSize = food.ServingSize,
                    ServingUnit = food.ServingUnit,
                    EnergyKcal = food.EnergyKcal,
                    FatG = food.FatG,
                    CarbohydrateG = food.CarbohydrateG,
                    ProteinG = food.ProteinG,
                    Nutrients = new Dictionary<string, decimal>(food.Nutrients, StringComparer.Ordinal)
                }
            ],
            ImageIds = [.. imageIds]
        };
}
