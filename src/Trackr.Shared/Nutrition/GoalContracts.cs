using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Trackr.Shared.Nutrition;

/// <summary>Which side of a target the user wants to be on.</summary>
/// <remarks>
/// Both directions are real and they read in opposite ways: a protein target is met by passing it,
/// a sodium target is broken by the same. A target with no direction would have to guess.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<GoalKind>))]
public enum GoalKind
{
    /// <summary>A floor: eat at least this much. Protein, fibre, most vitamins.</summary>
    AtLeast,

    /// <summary>A ceiling: eat at most this much. Calories, sodium, saturated fat.</summary>
    AtMost
}

/// <summary>One daily target.</summary>
/// <param name="NutrientKey">As <c>GET /api/nutrients</c> keys it. The core four are allowed.</param>
/// <param name="Target">In the nutrient's own unit. Never converted.</param>
public sealed record GoalResponse(string NutrientKey, decimal Target, GoalKind Kind);

/// <summary>
/// The whole set of targets, replacing whatever was there.
/// </summary>
/// <remarks>
/// A wholesale replace rather than per-goal routes, matching the catalog's nutrient map and the
/// log's items. A merge leaves "stop tracking this one" inexpressible without a second route whose
/// only job is deletion.
/// </remarks>
public sealed class SaveGoalsRequest
{
    [MaxLength(60)]
    public List<SaveGoalRequest> Goals { get; set; } = [];
}

/// <summary>One target on the way in.</summary>
public sealed class SaveGoalRequest
{
    [Required]
    [StringLength(60)]
    public string NutrientKey { get; set; } = "";

    /// <summary>Must be greater than zero. A target of nothing is the absence of a target.</summary>
    public decimal Target { get; set; }

    public GoalKind Kind { get; set; } = GoalKind.AtLeast;
}

/// <summary>A target set against what has actually been eaten.</summary>
/// <param name="Consumed">The day's total for this nutrient, or zero where nothing reported it.</param>
/// <param name="Fraction">
/// Consumed over target, uncapped. Over one is past the target - which is the goal for a floor and
/// the problem for a ceiling, and the client draws those differently.
/// </param>
/// <param name="IsMet">
/// Whether the day currently satisfies the target. <strong>A floor is not met until it is
/// reached</strong>, so a day in progress reads as unmet rather than as failed, which is the
/// distinction that keeps a progress bar from being an accusation at breakfast.
/// </param>
public sealed record GoalProgressResponse(
    string NutrientKey,
    decimal Target,
    GoalKind Kind,
    decimal Consumed,
    double Fraction,
    bool IsMet);
