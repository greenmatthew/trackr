using Trackr.Api.Identity;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Data;

/// <summary>
/// A daily target for one nutrient, and which side of it the user wants to be on.
/// </summary>
/// <remarks>
/// Keyed by nutrient rather than by a fixed set of columns, for the reason CLAUDE.md section 7
/// gives about nutrients generally: a target for selenium should be a row, not a migration. The
/// core four are catalog rows as well as columns precisely so that something like this can treat
/// them uniformly.
/// <para>
/// <strong>Per day, always.</strong> A weekly or a per-meal target is a different feature with a
/// different arithmetic, and section 9.12 asks for progress against a day - which is also the only
/// window the stats views total by default.
/// </para>
/// </remarks>
public sealed class Goal
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public TrackrUser? User { get; set; }

    /// <summary>Which nutrient, as <c>GET /api/nutrients</c> keys it. Core keys are allowed.</summary>
    public string NutrientKey { get; set; } = "";

    public Nutrient? Nutrient { get; set; }

    /// <summary>
    /// How much of it in one day, in the nutrient's own unit.
    /// </summary>
    /// <remarks>
    /// Never converted. The unit belongs to the nutrient and is reported by the catalog, so a
    /// target of 2 300 for sodium is 2 300 mg because sodium is milligrams - the same rule every
    /// stored amount in this codebase follows.
    /// </remarks>
    public decimal Target { get; set; }

    /// <summary>
    /// Whether the target is a floor or a ceiling.
    /// </summary>
    /// <remarks>
    /// Both are real and they read in opposite directions: "at least 100 g of protein" is met by
    /// exceeding it and "at most 2 000 kcal" is broken by the same. A single "target" with no
    /// direction would have to guess, and it would guess wrong for roughly half of them.
    /// </remarks>
    public GoalKind Kind { get; set; } = GoalKind.AtLeast;

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}
