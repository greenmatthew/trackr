using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Time;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Meal photos: upload, fetch, remove.
/// </summary>
/// <remarks>
/// An image is uploaded <em>before</em> the log entry it will belong to exists, and that ordering
/// is the point. The chat flow is upload, then run the cascade, then confirm: the photo has to be
/// on the server for the model to be retried without a second upload, and an abandoned
/// confirmation should leave no entry behind. <c>POST /api/log</c> adopts images by id afterwards.
/// <para>
/// The bytes are stored exactly as they arrive - no resize, no re-encode, no inspection. The
/// server has no image library and this milestone deliberately does not give it one: decoders are
/// a well-known remote-code-execution and denial-of-service surface, and the avatar endpoints
/// already set the precedent of validating the declared type and the length and nothing more.
/// Milestone 7 will need server-side decoding for barcode reading, and that is the moment to
/// revisit normalising on ingest - not before.
/// </para>
/// <para>
/// Photos are personal and are never shared, unlike catalog items. Another account's image is a
/// 404, not a 403.
/// </para>
/// </remarks>
public static class ImageEndpoints
{
    public static IEndpointRouteBuilder MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/images", UploadImageAsync)
            .WithName("UploadImage")
            .WithSummary("Upload a meal photo. Body is the raw image bytes; it starts unattached.");

        app.MapGet("/api/images/{id:guid}", GetImageAsync)
            .WithName("GetImage")
            .WithSummary("The photo's bytes. Supports If-None-Match.");

        app.MapDelete("/api/images/{id:guid}", DeleteImageAsync)
            .WithName("DeleteImage")
            .WithSummary("Remove a photo.");

        return app;
    }

    private static async Task<IResult> UploadImageAsync(
        HttpRequest request,
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

        // Strip any "; charset=" - a client that sends one is not wrong, and rejecting the upload
        // over it would be a baffling failure.
        var contentType = request.ContentType?.Split(';')[0].Trim();

        if (!MealImageRules.IsAllowedContentType(contentType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contentType"] =
                [
                    "That image format is not supported. Use "
                        + string.Join(", ", MealImageRules.AllowedContentTypes) + "."
                ]
            });
        }

        // Content-Length is a claim, not a fact, so it is only a fast rejection - the capped read
        // below is what actually enforces the limit.
        if (request.ContentLength > MealImageRules.MaxBytes)
        {
            return TooLarge();
        }

        var content = await ReadCappedAsync(request.Body, MealImageRules.MaxBytes, cancellationToken);
        if (content is null)
        {
            return TooLarge();
        }

        if (content.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = ["The image was empty."]
            });
        }

        var image = new MealImage
        {
            UserId = user.Id,
            ContentType = contentType!,
            Content = content,
            ByteCount = content.Length,
            CreatedUtc = Timestamps.UtcNow()
        };

        db.MealImages.Add(image);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created(
            $"/api/images/{image.Id}",
            new MealImageResponse(image.Id, image.ContentType, image.ByteCount, image.CreatedUtc));
    }

    private static async Task<IResult> GetImageAsync(
        Guid id,
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

        // The one place in the application that selects MealImage.Content. Everywhere else
        // projects metadata only, so a log response never carries megabytes it has no use for.
        var image = await db.MealImages
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.UserId == user.Id, cancellationToken);

        if (image is null)
        {
            return Results.NotFound();
        }

        // Results.Bytes answers 304 itself when the tag matches. A meal photo never changes once
        // uploaded, so the tag is derived from the id and the client can cache it indefinitely.
        return Results.Bytes(
            image.Content,
            image.ContentType,
            entityTag: new EntityTagHeaderValue($"\"{image.Id}\""));
    }

    private static async Task<IResult> DeleteImageAsync(
        Guid id,
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

        var image = await db.MealImages
            .FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.UserId == user.Id, cancellationToken);

        if (image is not null)
        {
            db.MealImages.Remove(image);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Idempotent, like the avatar delete: the caller wanted the photo gone and it is gone.
        return Results.NoContent();
    }

    private static IResult TooLarge() =>
        Results.Problem(
            title: "Image too large",
            detail: $"Meal photos must be under {MealImageRules.MaxBytes / (1024 * 1024)} MB.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Reads the body, giving up as soon as it exceeds <paramref name="maxBytes"/>.
    /// </summary>
    /// <returns>The bytes, or null if the body was longer than the cap.</returns>
    /// <remarks>
    /// The same capped read the avatar upload uses, and worth repeating rather than sharing: the
    /// two have different limits and different callers, and the whole body of the method is the
    /// cap. "An authenticated user can make the server allocate arbitrary memory" is still not a
    /// sentence worth being true.
    /// </remarks>
    private static async Task<byte[]?> ReadCappedAsync(
        Stream body,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        var chunk = new byte[64 * 1024];
        int read;

        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
