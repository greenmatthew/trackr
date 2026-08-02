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
            FatG = 9.25m,
            CarbohydrateG = 22.125m,
            ProteinG = 7.5m,
            Nutrients = nutrients ?? new Dictionary<string, decimal>(StringComparer.Ordinal)
        };

    /// <summary>An entry logging <paramref name="quantity"/> servings of a catalog item.</summary>
    /// <remarks>
    /// Nutrient values are per serving, as the API expects; the server multiplies them in.
    /// </remarks>
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
