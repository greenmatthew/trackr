using System.Reflection;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Keeps an analysed item and a saveable log item the same shape.
/// </summary>
/// <remarks>
/// Milestone 9's confirmation card takes what <c>POST /api/analyze</c> returned, lets the user
/// correct it, and posts it to <c>/api/log</c>. That should be a field-for-field copy and nothing
/// more, because a mapping layer between them is a second place for a number to change - which is
/// the failure CLAUDE.md section 2 names as the one to avoid.
/// <para>
/// So this is a drift test rather than a behaviour test. It fails when somebody renames a field on
/// one type, or gives it a different kind of number, and does not notice the other.
/// </para>
/// </remarks>
public sealed class MealAnalysisContractTests
{
    /// <remarks>
    /// <c>FoodItemId</c> is the one exception, and its absence is deliberate: the cascade does not
    /// look in the catalog, so it has no id to report. Milestone 10 is what fills that in, by
    /// upserting an item at the moment the user confirms.
    /// </remarks>
    [Fact]
    public void Every_field_a_log_item_needs_is_on_an_analysed_item()
    {
        var analysed = typeof(MealAnalysisItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, StringComparer.Ordinal);

        var missing = typeof(SaveLogItemRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name is not nameof(SaveLogItemRequest.FoodItemId))
            .Where(property => !analysed.ContainsKey(property.Name))
            .Select(property => property.Name)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            $"A confirmed item could not supply {string.Join(", ", missing)} without inventing it.");
    }

    /// <remarks>
    /// Same name is not enough - a <c>double</c> where the other has a <c>decimal</c> would compile,
    /// round differently, and put a slightly wrong number in the database. The nutrient map is the
    /// collections that legitimately differ, because a response hands out read-only views of what a
    /// request supplies as mutable ones.
    /// </remarks>
    [Fact]
    public void The_numbers_on_both_are_the_same_kind_of_number()
    {
        var analysed = typeof(MealAnalysisItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(property => property.Name, property => property.PropertyType, StringComparer.Ordinal);

        foreach (var property in typeof(SaveLogItemRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name is nameof(SaveLogItemRequest.FoodItemId)
                    or nameof(SaveLogItemRequest.Nutrients)
                    or nameof(SaveLogItemRequest.Allergens)
                    or nameof(SaveLogItemRequest.DietFlags)
                || !analysed.TryGetValue(property.Name, out var type))
            {
                continue;
            }

            Assert.True(
                type == property.PropertyType,
                $"{property.Name} is {type.Name} when analysed and {property.PropertyType.Name} when saved.");
        }
    }

    /// <summary>
    /// The nutrient maps must at least agree about what a nutrient amount is.
    /// </summary>
    [Fact]
    public void Both_nutrient_maps_hold_decimals_keyed_by_string()
    {
        Assert.Equal(
            [typeof(string), typeof(decimal)],
            typeof(MealAnalysisItem).GetProperty(nameof(MealAnalysisItem.Nutrients))!
                .PropertyType.GetGenericArguments());

        Assert.Equal(
            [typeof(string), typeof(decimal)],
            typeof(SaveLogItemRequest).GetProperty(nameof(SaveLogItemRequest.Nutrients))!
                .PropertyType.GetGenericArguments());
    }
}
