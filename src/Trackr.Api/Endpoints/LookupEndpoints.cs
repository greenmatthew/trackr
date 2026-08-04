using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Cascade;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Security;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Stages one and two of the cascade, as routes: read a barcode out of a photo, and ask Open Food
/// Facts what it is.
/// </summary>
/// <remarks>
/// <strong>Read-only. Nothing here writes to the catalog or the log.</strong> That is CLAUDE.md
/// section 2's confirm-before-save rule, and it is also why filling the catalog from a lookup is
/// milestone 10 rather than an obvious convenience to add here: a lookup the user then corrects or
/// abandons must leave nothing behind.
/// <para>
/// These are not a barcode-entry surface (section 10 forbids one) and the app must not present them
/// as one. They exist because the chat in milestone 9 needs somewhere to send a photo, and because a
/// milestone whose only test is a unit test is a milestone nobody has actually run.
/// </para>
/// <para>
/// Not merged into <c>/api/foods</c>: a lookup is a question about the outside world, not a resource
/// in this server's catalog, and the two have different rate limits for that reason.
/// </para>
/// </remarks>
public static class LookupEndpoints
{
    public static IEndpointRouteBuilder MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/lookup/barcode/{barcode}", LookupBarcodeAsync)
            .RequireRateLimiting(RateLimitPolicies.Lookup)
            .WithName("LookupBarcode")
            .WithSummary("Ask Open Food Facts about a barcode. Writes nothing.");

        app.MapPost("/api/lookup/image/{id:guid}", ScanStoredImageAsync)
            .RequireRateLimiting(RateLimitPolicies.Lookup)
            .WithName("ScanStoredImage")
            .WithSummary("Read a barcode out of an uploaded meal photo and look it up. Writes nothing.");

        return app;
    }

    private static async Task<IResult> LookupBarcodeAsync(
        string barcode,
        IProductLookup lookup,
        CancellationToken cancellationToken)
    {
        // Validated rather than trusted, even though the route is authenticated: this value is about
        // to be interpolated into an outbound URL.
        if (!OpenFoodFactsClient.IsPlausibleBarcode(barcode))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["barcode"] = ["A barcode is 8 to 14 digits."]
            });
        }

        return Results.Ok(await lookup.FindByBarcodeAsync(barcode, cancellationToken));
    }

    /// <summary>
    /// Runs stages one and two over a photo the caller already uploaded to <c>/api/images</c>.
    /// </summary>
    /// <remarks>
    /// Takes an image id rather than the bytes, so a photo is uploaded once and can be re-examined
    /// without being sent again - the same reasoning that made <c>ImageEndpoints</c> accept an upload
    /// before the log entry it belongs to exists.
    /// <para>
    /// A photo with no barcode in it is a perfectly ordinary answer, not a 404: most meals are not
    /// packaged. The caller gets <see cref="ProductLookupOutcome.NotFound"/> and a null barcode,
    /// which is its signal to send the photo to the model instead.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ScanStoredImageAsync(
        Guid id,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        IBarcodeDecoder decoder,
        IProductLookup lookup,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Another account's photo is a 404, matching ImageEndpoints - a meal photo is personal and
        // its existence is not something to confirm to anybody else.
        var image = await db.MealImages
            .AsNoTracking()
            .Where(candidate => candidate.Id == id && candidate.UserId == user.Id)
            .Select(candidate => new { candidate.Content })
            .FirstOrDefaultAsync(cancellationToken);

        if (image is null)
        {
            return Results.NotFound();
        }

        var decoded = decoder.Decode(image.Content);

        if (decoded.Barcode is null)
        {
            // Section 5: an unreadable image is a real problem and gets said out loud, while "this
            // photo simply has no barcode" is the normal case and gets no warning at all.
            return Results.Ok(new BarcodeScanResult(
                null,
                ProductLookupResult.NotFound(decoded.Problem is null ? null : [decoded.Problem])));
        }

        var result = await lookup.FindByBarcodeAsync(decoded.Barcode, cancellationToken);

        return Results.Ok(new BarcodeScanResult(decoded.Barcode, result));
    }
}
