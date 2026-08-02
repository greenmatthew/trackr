using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// The nutrient catalog: what Trackr can record, what each thing is called, and what unit it is
/// measured in.
/// </summary>
/// <remarks>
/// Read-only, deliberately. A client-created nutrient would put a key in the store for which no
/// unit conversion exists anywhere in the code, and CLAUDE.md section 7's "data-driven" means the
/// set is a data change made by a deployment, not by a request.
/// <para>
/// Still behind authentication, like everything else: CLAUDE.md section 8 permits no
/// unauthenticated endpoints except login. The fallback policy in Program.cs covers this without
/// an attribute.
/// </para>
/// <para>
/// A client is expected to fetch this once and cache it - it is the source of display names and
/// sort order for every nutrient map the API returns, since those maps carry keys and amounts only.
/// </para>
/// </remarks>
public static class NutrientEndpoints
{
    public static IEndpointRouteBuilder MapNutrientEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/nutrients", ListNutrientsAsync)
            .WithName("ListNutrients")
            .WithSummary("Every nutrient the server can record, in nutrition-label order.");

        return app;
    }

    private static async Task<IResult> ListNutrientsAsync(
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        // From the table rather than from NutrientCatalog: this is the one place worth reporting
        // what was actually seeded, so a failed seed shows up as an empty list here instead of as
        // a foreign-key violation on somebody's first save.
        var nutrients = await db.Nutrients
            .AsNoTracking()
            .OrderBy(nutrient => nutrient.SortOrder)
            .Select(nutrient => new NutrientResponse(
                nutrient.Key,
                nutrient.DisplayName,
                nutrient.Unit,
                nutrient.Group,
                nutrient.SortOrder,
                nutrient.IsCore))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(nutrients);
    }
}
