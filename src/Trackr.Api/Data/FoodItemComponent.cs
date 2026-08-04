namespace Trackr.Api.Data;

/// <summary>
/// One ingredient of a composite item: how many servings of a child <see cref="FoodItem"/> go into
/// one batch of the parent.
/// </summary>
/// <remarks>
/// The primary key is the pair (<see cref="ParentFoodItemId"/>, <see cref="ChildFoodItemId"/>), with
/// no surrogate - the same reasoning as <see cref="FoodItemNutrient"/>. An ingredient appearing twice
/// in one recipe is a client that should have added the quantities up, not two rows.
/// <para>
/// <strong>Quantity is in servings of the child, not in grams.</strong> A serving is the only unit
/// every catalog item is guaranteed to have, and it is the unit the child's nutrient values are
/// already expressed in - so composing is a multiplication rather than a unit conversion nobody has
/// the data to do. "150 g of flour" is entered as the number of flour servings that comes to.
/// </para>
/// <para>
/// These rows are the <em>only</em> place the composite structure lives. The parent's nutrition is
/// materialised onto its own columns when this table changes, so nothing downstream - the log, the
/// aggregates, the whole cascade - ever learns that composites exist.
/// </para>
/// </remarks>
public class FoodItemComponent
{
    /// <summary>The recipe.</summary>
    /// <remarks>
    /// Cascade: deleting a recipe takes its ingredient list, which is meaningless without it.
    /// </remarks>
    public Guid ParentFoodItemId { get; set; }

    public FoodItem? Parent { get; set; }

    /// <summary>The ingredient.</summary>
    /// <remarks>
    /// Cascade at the database level, but the API refuses (409) to delete an item any recipe uses,
    /// so the only thing that ever reaches this cascade is an account deletion. That is safe because
    /// a global recipe may only hold global ingredients: no recipe can outlive an account whose
    /// personal item it depends on, and so no cascade here can silently leave a recipe with numbers
    /// that no longer add up.
    /// </remarks>
    public Guid ChildFoodItemId { get; set; }

    public FoodItem? Child { get; set; }

    /// <summary>
    /// How many servings of the child go into one batch of the parent - a batch being
    /// <see cref="FoodItem.Yield"/> servings of it.
    /// </summary>
    public decimal Quantity { get; set; }
}
