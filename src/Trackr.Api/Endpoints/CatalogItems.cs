using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// The two things everything touching the catalog has to agree about: who may see an item, and
/// what a new one looks like.
/// </summary>
/// <remarks>
/// Extracted because milestone 10 gave the catalog a second writer. Until then
/// <see cref="FoodEndpoints"/> was the only place a <see cref="FoodItem"/> came into existence and
/// keeping the rules private there cost nothing; a second copy of "round this, trim that, stamp the
/// editor" is how two writers drift into storing subtly different rows for the same food.
/// <para>
/// The visibility predicate had already been copied once, into <c>LogEndpoints</c>, which is the
/// warning this class is answering rather than widening.
/// </para>
/// </remarks>
internal static class CatalogItems
{
    /// <summary>Everything this account may see: its own items, plus the shared catalog.</summary>
    public static IQueryable<FoodItem> VisibleTo(TrackrDbContext db, Guid userId) =>
        db.FoodItems.Where(item => item.UserId == userId || item.UserId == null);

    /// <summary>
    /// A new catalog item, with every stored measurement rounded to what its column keeps.
    /// </summary>
    /// <param name="owner">The account it belongs to, or null for a global item.</param>
    /// <param name="editorId">
    /// Who is creating it. Recorded even on a global item, so a shared row can always name the
    /// account that last touched it.
    /// </param>
    public static FoodItem New(
        string name,
        string? brand,
        string? barcode,
        decimal servingSize,
        string servingUnit,
        FoodSource source,
        Guid? owner,
        Guid editorId,
        DateTimeOffset now) =>
        new()
        {
            UserId = owner,
            Name = name,
            Brand = brand,
            Barcode = barcode,
            // Rounded here so a response is exactly what a later GET will report - see
            // StoredPrecision.
            ServingSize = StoredPrecision.Measure(servingSize),
            ServingUnit = servingUnit,
            Source = source,
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByUserId = editorId
        };

    /// <summary>
    /// Replaces what an item is made of.
    /// </summary>
    /// <remarks>
    /// Wholesale, like the nutrient map: a merge would leave "this list was wrong, here is the
    /// right one" inexpressible. The caller is expected to have run the values through
    /// <c>NutritionValidation.NormaliseIngredients</c> first, which is where the rule about which
    /// items may carry a list at all lives.
    /// </remarks>
    public static void ApplyIngredients(
        FoodItem item,
        string? ingredientsText,
        List<string> allergens,
        List<string> dietFlags)
    {
        item.IngredientsText = ingredientsText;
        item.Allergens = allergens;
        item.DietFlags = dietFlags;
    }

    /// <summary>
    /// Replaces an item's nutrition with the amounts supplied.
    /// </summary>
    /// <remarks>
    /// Clear and re-add inside one SaveChanges, deliberately not ExecuteDeleteAsync: that runs
    /// outside the SaveChanges transaction, so a failure on the insert would leave the item with
    /// half a nutrient map and no error to explain it.
    /// </remarks>
    public static void ApplyNutrition(
        FoodItem item,
        decimal energyKcal,
        decimal fatG,
        decimal carbohydrateG,
        decimal proteinG,
        IReadOnlyDictionary<string, decimal> nutrients)
    {
        item.EnergyKcal = StoredPrecision.Amount(energyKcal);
        item.FatG = StoredPrecision.Amount(fatG);
        item.CarbohydrateG = StoredPrecision.Amount(carbohydrateG);
        item.ProteinG = StoredPrecision.Amount(proteinG);

        item.Nutrients.Clear();

        foreach (var (key, amount) in nutrients)
        {
            item.Nutrients.Add(new FoodItemNutrient
            {
                FoodItemId = item.Id,
                NutrientKey = key,
                Amount = StoredPrecision.Amount(amount)
            });
        }
    }
}
