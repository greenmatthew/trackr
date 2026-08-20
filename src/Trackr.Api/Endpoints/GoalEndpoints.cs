using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Time;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Daily targets, and how today is going against them.
/// </summary>
/// <remarks>
/// Milestone 12, and CLAUDE.md section 9 is explicit that it comes after the stats views rather
/// than instead of them: a target is only meaningful once the number it is a target for exists and
/// is trusted.
/// <para>
/// Keyed by nutrient throughout, using the same vocabulary as everything else, so a target for
/// selenium is a row rather than a migration. The core four are catalog rows as well as columns
/// precisely so this can treat them uniformly.
/// </para>
/// </remarks>
public static class GoalEndpoints
{
    /// <summary>
    /// A sanity limit rather than a considered ceiling on how many targets a person may have.
    /// </summary>
    /// <remarks>
    /// There are 29 nutrients and one target each is the most that can be meaningful, so anything
    /// approaching this is a client looping rather than a person planning.
    /// </remarks>
    private const int MaxGoals = 60;

    public static IEndpointRouteBuilder MapGoalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/goals", ListGoalsAsync)
            .WithName("ListGoals")
            .WithSummary("The caller's daily targets.");

        app.MapPut("/api/goals", ReplaceGoalsAsync)
            .WithName("ReplaceGoals")
            .WithSummary("Replace the whole set of targets.");

        app.MapGet("/api/goals/progress", GetProgressAsync)
            .WithName("GetGoalProgress")
            .WithSummary("Each target against what has been eaten on a local day. Defaults to today.");

        return app;
    }

    private static async Task<IResult> ListGoalsAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await QueryFor(db, user.Id)
            .Select(goal => new GoalResponse(goal.NutrientKey, goal.Target, goal.Kind))
            .ToListAsync(cancellationToken));
    }

    /// <remarks>
    /// A wholesale replace, like the catalog's nutrient map and the log's items. A merge would leave
    /// "stop tracking this one" inexpressible without a second route whose only job is deletion.
    /// </remarks>
    private static async Task<IResult> ReplaceGoalsAsync(
        SaveGoalsRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        NutrientCatalog catalog,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var errors = new ValidationErrors();

        Validate(request, catalog, errors);

        if (errors.Any)
        {
            return errors.Problem();
        }

        var existing = await QueryFor(db, user.Id).ToListAsync(cancellationToken);

        db.Goals.RemoveRange(existing);

        var now = Timestamps.UtcNow();

        foreach (var goal in request.Goals)
        {
            db.Goals.Add(new Goal
            {
                UserId = user.Id,
                NutrientKey = goal.NutrientKey.Trim(),
                Target = StoredPrecision.Amount(goal.Target),
                Kind = goal.Kind,
                CreatedUtc = now,
                UpdatedUtc = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await QueryFor(db, user.Id)
            .Select(goal => new GoalResponse(goal.NutrientKey, goal.Target, goal.Kind))
            .ToListAsync(cancellationToken));
    }

    /// <param name="date">Which local day to measure against. Defaults to today.</param>
    /// <remarks>
    /// Measured on the server for the same reason the totals are: the day boundary follows the
    /// account's time zone, and a client that named its own day would report progress against the
    /// wrong one for most of every evening - which milestone 11 found out the hard way.
    /// </remarks>
    private static async Task<IResult> GetProgressAsync(
        DateOnly? date,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        DayBoundary boundary,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var goals = await QueryFor(db, user.Id).ToListAsync(cancellationToken);

        if (goals.Count == 0)
        {
            return Results.Ok(Array.Empty<GoalProgressResponse>());
        }

        var day = date ?? boundary.TodayFor(user);
        var (fromUtc, toUtc) = boundary.DayFor(user, day);

        var items = await db.LogItems
            .Where(item => item.LogEntry!.UserId == user.Id
                && item.LogEntry.LoggedUtc >= fromUtc
                && item.LogEntry.LoggedUtc < toUtc)
            .Select(item => new
            {
                item.EnergyKcal,
                item.FatG,
                item.CarbohydrateG,
                item.ProteinG,
                Nutrients = item.Nutrients
                    .Select(nutrient => new { nutrient.NutrientKey, nutrient.Amount })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var eaten = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            [CoreNutrients.EnergyKcal] = items.Sum(item => item.EnergyKcal),
            [CoreNutrients.Fat] = items.Sum(item => item.FatG),
            [CoreNutrients.Carbohydrate] = items.Sum(item => item.CarbohydrateG),
            [CoreNutrients.Protein] = items.Sum(item => item.ProteinG)
        };

        foreach (var nutrient in items.SelectMany(item => item.Nutrients))
        {
            eaten[nutrient.NutrientKey] = eaten.GetValueOrDefault(nutrient.NutrientKey) + nutrient.Amount;
        }

        return Results.Ok(goals.Select(goal => Progress(goal, eaten)).ToList());
    }

    /// <remarks>
    /// <strong>Zero where nothing reported it, and that is not the "missing is not zero" rule being
    /// broken.</strong> The question a target asks is "how much did you eat", and a nutrient nobody
    /// recorded contributed nothing to the day whether or not it was present in the food. Saying so
    /// is honest; the dishonest version would be reporting a target as met on the strength of food
    /// nobody described.
    /// </remarks>
    private static GoalProgressResponse Progress(Goal goal, IReadOnlyDictionary<string, decimal> eaten)
    {
        var consumed = StoredPrecision.Amount(eaten.GetValueOrDefault(goal.NutrientKey));

        // Uncapped. Past a floor is the point and past a ceiling is the problem, and a client that
        // could not see beyond 100% could not draw the difference.
        var fraction = goal.Target <= 0 ? 0d : (double)(consumed / goal.Target);

        return new GoalProgressResponse(
            goal.NutrientKey,
            goal.Target,
            goal.Kind,
            consumed,
            fraction,
            goal.Kind is GoalKind.AtLeast ? consumed >= goal.Target : consumed <= goal.Target);
    }

    private static IQueryable<Goal> QueryFor(TrackrDbContext db, Guid userId) =>
        db.Goals.Where(goal => goal.UserId == userId).OrderBy(goal => goal.NutrientKey);

    private static void Validate(SaveGoalsRequest request, NutrientCatalog catalog, ValidationErrors errors)
    {
        if (request.Goals.Count > MaxGoals)
        {
            errors.Add("goals", $"That is more than {MaxGoals} targets, which is more than there are nutrients.");

            return;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < request.Goals.Count; index++)
        {
            var goal = request.Goals[index];
            var field = $"goals[{index}]";
            var key = goal.NutrientKey?.Trim() ?? "";

            if (!catalog.Contains(key))
            {
                errors.Add($"{field}.nutrientKey", "This server does not track that nutrient.");
            }
            else if (!seen.Add(key))
            {
                // Two targets for one nutrient is a contradiction rather than a refinement: "at
                // least 100 g" and "at most 80 g" cannot both be satisfied.
                errors.Add($"{field}.nutrientKey", "There is already a target for that nutrient.");
            }

            if (goal.Target <= 0)
            {
                errors.Add($"{field}.target", "A target has to be bigger than nothing.");
            }
        }
    }
}
