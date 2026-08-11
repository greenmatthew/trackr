using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trackr.Api.Cascade;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Security;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// The whole logging cascade as one route: text and photos in, a confirmable answer out.
/// </summary>
/// <remarks>
/// <strong>Read-only, like the lookup routes and for the same reason.</strong> CLAUDE.md section 2
/// requires the user to see and correct what was parsed before anything is written, so this endpoint
/// returns numbers and stores none of them. Milestone 9's confirmation card is what turns a result
/// into a <c>POST /api/log</c>; there is a test whose only job is to assert this route leaves the
/// catalog and the log untouched.
/// <para>
/// One route rather than three, because a client that ran the stages itself would be a second copy
/// of the cascade - the thing CLAUDE.md section 10 forbids putting on the phone, where changing it
/// means shipping an APK.
/// </para>
/// </remarks>
public static class AnalyzeEndpoints
{
    public static IEndpointRouteBuilder MapAnalyzeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/analyze", AnalyzeAsync)
            .RequireRateLimiting(RateLimitPolicies.Analysis)
            .WithName("AnalyzeMeal")
            .WithSummary("Work out what a description and some photos add up to. Writes nothing.");

        return app;
    }

    private static async Task<IResult> AnalyzeAsync(
        AnalyzeMealRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        MealCascade cascade,
        IOptions<OllamaOptions> options,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var errors = new ValidationErrors();
        var imageIds = request.ImageIds.Distinct().ToList();
        var text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();

        if (text is null && imageIds.Count == 0)
        {
            errors.Add("text", "Say what you ate, attach a photo, or both.");
        }

        if (imageIds.Count > options.Value.MaxImages)
        {
            // A cap rather than silently ignoring the extras. Each photo costs most of the
            // wall-clock time of an analysis on a CPU-only server, so this is the difference between
            // a slow answer and one nobody waits for.
            errors.Add(
                "imageIds",
                $"At most {options.Value.MaxImages} photos can be looked at in one go.");
        }

        if (errors.Any)
        {
            return errors.Problem();
        }

        // Another account's photo is a 404, matching ImageEndpoints: a meal photo is personal, and
        // whether one exists is not something to confirm to somebody else.
        var stored = await db.MealImages
            .AsNoTracking()
            .Where(image => imageIds.Contains(image.Id) && image.UserId == user.Id)
            .Select(image => new { image.Id, image.ContentType, image.Content })
            .ToListAsync(cancellationToken);

        if (stored.Count != imageIds.Count)
        {
            return Results.NotFound();
        }

        // Kept in the order the client sent them, because that is the order the user attached them
        // in and the model is told to work through them.
        var photos = imageIds
            .Select(id => stored.First(image => image.Id == id))
            .Select(image => new MealPhoto(image.Id, image.ContentType, image.Content))
            .ToList();

        var result = await cascade.AnalyzeAsync(text, photos, cancellationToken);

        // A failed analysis is still a 200 carrying an explanation, not an error status. Nothing
        // went wrong with the request - the model could not read a blurry label, which is an answer
        // the chat has to render as a message rather than as a failed HTTP call.
        return Results.Ok(result);
    }
}
