using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Time;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// The food log: what was eaten, when, and what it contained.
/// </summary>
/// <remarks>
/// A log entry is one logging occasion - in the finished app, one confirmed chat card. Its items
/// carry a frozen snapshot of every nutrient, so nothing that happens to the catalog afterwards
/// can change what a day already said (CLAUDE.md section 7).
/// <para>
/// Entry, items and photo attachments are written in one request. An entry with no items is
/// meaningless, and a two-step create would leave an orphaned entry behind every time the second
/// call failed.
/// </para>
/// <para>
/// <strong>Quantity is multiplied in on write.</strong> A request carries per-serving values, the
/// same shape a catalog item has; what is stored is the total for the quantity eaten. That is what
/// makes the stats views a plain <c>SUM</c> and what lets an ad-hoc item exist without inventing a
/// serving size.
/// </para>
/// <para>
/// The plural/singular split with <c>/api/foods</c> is deliberate: a food item is one of many you
/// have, while the log is one thing you append to.
/// </para>
/// </remarks>
public static class LogEndpoints
{
    /// <summary>
    /// The widest span a single request may ask for.
    /// </summary>
    /// <remarks>
    /// A year and a day, so "the last twelve months" fits. Without a cap, one mistyped date turns
    /// the month view into an all-time scan that reads every nutrient row the account owns.
    /// </remarks>
    private const int MaxDaysInRange = 366;

    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/log", ListLogAsync)
            .WithName("ListLog")
            .WithSummary("Log entries for a range of local days. Defaults to today.");

        app.MapGet("/api/log/{id:guid}", GetLogEntryAsync)
            .WithName("GetLogEntry")
            .WithSummary("One log entry, with its items and photo metadata.");

        app.MapPost("/api/log", CreateLogEntryAsync)
            .WithName("CreateLogEntry")
            .WithSummary("Record a meal: the entry, its items and any photos, in one request.");

        app.MapPut("/api/log/{id:guid}", ReplaceLogEntryAsync)
            .WithName("ReplaceLogEntry")
            .WithSummary("Replace an entry, its items and its photo set.");

        app.MapDelete("/api/log/{id:guid}", DeleteLogEntryAsync)
            .WithName("DeleteLogEntry")
            .WithSummary("Delete an entry, its items and its photos.");

        return app;
    }

    /// <param name="from">First local day to include. Defaults to today.</param>
    /// <param name="to">Last local day to include, inclusive. Defaults to <paramref name="from"/>.</param>
    /// <remarks>
    /// One parameter shape covers day, week and month, which is exactly what milestone 11 needs.
    /// The days are turned into instants by <see cref="DayBoundary"/> - the only place that knows
    /// what "today" means - and the interval it produces is half-open, so nothing logged in the
    /// last microsecond of a day can fall through the gap.
    /// </remarks>
    private static async Task<IResult> ListLogAsync(
        DateOnly? from,
        DateOnly? to,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        DayBoundary days,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var firstDay = from ?? days.TodayFor(user);
        var lastDay = to ?? firstDay;

        if (lastDay < firstDay)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = ["The end of the range cannot be before the start."]
            });
        }

        if (lastDay.DayNumber - firstDay.DayNumber + 1 > MaxDaysInRange)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["to"] = [$"That range is too wide - {MaxDaysInRange} days at most."]
            });
        }

        var (fromInclusive, toExclusive) = days.RangeFor(user, firstDay, lastDay);

        var entries = await EntriesOf(db, user.Id)
            .Where(entry => entry.LoggedUtc >= fromInclusive && entry.LoggedUtc < toExclusive)
            .OrderBy(entry => entry.LoggedUtc)
            .Select(Projection)
            .ToListAsync(cancellationToken);

        return Results.Ok(entries.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GetLogEntryAsync(
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

        var entry = await EntriesOf(db, user.Id)
            .Where(candidate => candidate.Id == id)
            .Select(Projection)
            .FirstOrDefaultAsync(cancellationToken);

        // 404 for another account's entry, never 403 - the id should not be confirmable.
        return entry is null ? Results.NotFound() : Results.Ok(ToResponse(entry));
    }

    private static async Task<IResult> CreateLogEntryAsync(
        SaveLogEntryRequest request,
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
        await ValidateAsync(request, user.Id, entryId: null, db, catalog, errors, cancellationToken);

        if (errors.Any)
        {
            return errors.Problem();
        }

        var now = Timestamps.UtcNow();

        var entry = new LogEntry
        {
            UserId = user.Id,
            LoggedUtc = Timestamps.ToStorablePrecision(request.LoggedUtc ?? now),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedUtc = now,
            UpdatedUtc = now
        };

        foreach (var item in request.Items)
        {
            entry.Items.Add(Snapshot(item, now));
        }

        db.LogEntries.Add(entry);

        await AttachImagesAsync(db, user.Id, entry, request.ImageIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/log/{entry.Id}", await ReadBackAsync(db, user.Id, entry.Id, cancellationToken));
    }

    /// <remarks>
    /// A replace, like the catalog's PUT: items and their nutrient maps are rewritten wholesale,
    /// because a merge leaves "remove the item I logged by mistake" inexpressible.
    /// </remarks>
    private static async Task<IResult> ReplaceLogEntryAsync(
        Guid id,
        SaveLogEntryRequest request,
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
        await ValidateAsync(request, user.Id, entryId: id, db, catalog, errors, cancellationToken);

        if (errors.Any)
        {
            return errors.Problem();
        }

        var entry = await db.LogEntries
            .Include(candidate => candidate.Items)
            .Include(candidate => candidate.Images)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == id && candidate.UserId == user.Id,
                cancellationToken);

        if (entry is null)
        {
            return Results.NotFound();
        }

        var now = Timestamps.UtcNow();

        entry.LoggedUtc = Timestamps.ToStorablePrecision(request.LoggedUtc ?? entry.LoggedUtc);
        entry.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        entry.UpdatedUtc = now;

        // Through the change tracker inside one SaveChanges, not ExecuteDelete: the latter runs in
        // its own transaction, so a failure while inserting the replacements would leave the entry
        // holding half of them.
        db.LogItems.RemoveRange(entry.Items);
        entry.Items.Clear();

        foreach (var item in request.Items)
        {
            entry.Items.Add(Snapshot(item, now));
        }

        // Photos dropped from the set are detached rather than deleted. A photo cannot be
        // recreated from a request the way an item can, and a client that sent no image ids
        // because it does not know about images should not be able to destroy them. Milestone 14
        // sweeps images that end up attached to nothing.
        foreach (var image in entry.Images.Where(image => !request.ImageIds.Contains(image.Id)).ToList())
        {
            image.LogEntryId = null;
            entry.Images.Remove(image);
        }

        await AttachImagesAsync(db, user.Id, entry, request.ImageIds, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(await ReadBackAsync(db, user.Id, entry.Id, cancellationToken));
    }

    private static async Task<IResult> DeleteLogEntryAsync(
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

        var entry = await db.LogEntries
            .FirstOrDefaultAsync(candidate => candidate.Id == id && candidate.UserId == user.Id, cancellationToken);

        if (entry is null)
        {
            return Results.NotFound();
        }

        // Cascades to items, their nutrient rows and the entry's photos. Nothing else references
        // any of them - a photo exists for the entry it belongs to.
        db.LogEntries.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    /// <summary>Freezes one request item into a row, multiplying the quantity in.</summary>
    private static LogItem Snapshot(SaveLogItemRequest request, DateTimeOffset now)
    {
        var quantity = StoredPrecision.Measure(request.Quantity);

        var item = new LogItem
        {
            FoodItemId = request.FoodItemId,
            Name = request.Name.Trim(),
            Brand = string.IsNullOrWhiteSpace(request.Brand) ? null : request.Brand.Trim(),
            Quantity = quantity,
            ServingSize = request.ServingSize is { } size ? StoredPrecision.Measure(size) : null,
            ServingUnit = string.IsNullOrWhiteSpace(request.ServingUnit) ? null : request.ServingUnit.Trim(),
            EnergyKcal = StoredPrecision.Amount(request.EnergyKcal * quantity),
            FatG = StoredPrecision.Amount(request.FatG * quantity),
            CarbohydrateG = StoredPrecision.Amount(request.CarbohydrateG * quantity),
            ProteinG = StoredPrecision.Amount(request.ProteinG * quantity),
            CreatedUtc = now
        };

        foreach (var (key, amount) in request.Nutrients)
        {
            item.Nutrients.Add(new LogItemNutrient
            {
                LogItemId = item.Id,
                NutrientKey = key,
                Amount = StoredPrecision.Amount(amount * quantity)
            });
        }

        return item;
    }

    /// <summary>Claims the caller's unattached photos for this entry.</summary>
    /// <remarks>
    /// Validation has already established that every id belongs to the caller and is free, so this
    /// only has to do the assignment. Images already attached to <em>this</em> entry are skipped,
    /// which is what makes a PUT that keeps its photos a no-op rather than a conflict.
    /// </remarks>
    private static async Task AttachImagesAsync(
        TrackrDbContext db,
        Guid userId,
        LogEntry entry,
        List<Guid> imageIds,
        CancellationToken cancellationToken)
    {
        if (imageIds.Count == 0)
        {
            return;
        }

        var images = await db.MealImages
            .Where(image => imageIds.Contains(image.Id) && image.UserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var image in images.Where(image => image.LogEntryId != entry.Id))
        {
            image.LogEntryId = entry.Id;
        }
    }

    /// <param name="entryId">
    /// The entry being replaced, or null when creating. Photos already attached to that entry are
    /// not "taken" as far as this request is concerned - otherwise a PUT that kept its photos
    /// would be refused for conflicting with itself.
    /// </param>
    private static async Task ValidateAsync(
        SaveLogEntryRequest request,
        Guid userId,
        Guid? entryId,
        TrackrDbContext db,
        NutrientCatalog catalog,
        ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            errors.Add("items", "A log entry needs at least one item.");
        }

        if (request.Note?.Trim().Length > 500)
        {
            errors.Add("note", "That note is too long (500 characters at most).");
        }

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var field = $"items[{index}]";

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                errors.Add($"{field}.name", "A name is required.");
            }
            else if (item.Name.Trim().Length > 200)
            {
                errors.Add($"{field}.name", "That name is too long (200 characters at most).");
            }

            if (item.Quantity <= 0)
            {
                errors.Add($"{field}.quantity", "A quantity has to be bigger than nothing.");
            }

            if (item.ServingSize is <= 0)
            {
                errors.Add($"{field}.servingSize", "A serving has to be bigger than nothing.");
            }

            NutritionValidation.ValidateCoreNutrients(
                item.EnergyKcal,
                item.FatG,
                item.CarbohydrateG,
                item.ProteinG,
                errors);

            NutritionValidation.ValidateNutrients(item.Nutrients, catalog, $"{field}.nutrients", errors);
        }

        await ValidateFoodItemsAsync(request, userId, db, errors, cancellationToken);
        await ValidateImagesAsync(request, userId, entryId, db, errors, cancellationToken);
    }

    /// <summary>
    /// Every referenced catalog item must be visible to the caller.
    /// </summary>
    /// <remarks>
    /// The trap this closes: without it, an account could pass a food item id belonging to
    /// somebody else's <em>personal</em> catalog and read the name and brand back out of the
    /// snapshot in the response. Visible means "mine or global", so a shared item is of course
    /// accepted - checking ownership alone would break the whole point of the shared catalog.
    /// </remarks>
    private static async Task ValidateFoodItemsAsync(
        SaveLogEntryRequest request,
        Guid userId,
        TrackrDbContext db,
        ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        var referenced = request.Items
            .Select(item => item.FoodItemId)
            .OfType<Guid>()
            .Distinct()
            .ToList();

        if (referenced.Count == 0)
        {
            return;
        }

        var visible = await db.FoodItems
            .Where(item => referenced.Contains(item.Id) && (item.UserId == userId || item.UserId == null))
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        for (var index = 0; index < request.Items.Count; index++)
        {
            var id = request.Items[index].FoodItemId;

            if (id is not null && !visible.Contains(id.Value))
            {
                errors.Add(
                    $"items[{index}].foodItemId",
                    "There is no catalog item with that id that you can use.");
            }
        }
    }

    private static async Task ValidateImagesAsync(
        SaveLogEntryRequest request,
        Guid userId,
        Guid? entryId,
        TrackrDbContext db,
        ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        if (request.ImageIds.Count == 0)
        {
            return;
        }

        var ids = request.ImageIds.Distinct().ToList();

        if (ids.Count != request.ImageIds.Count)
        {
            errors.Add("imageIds", "The same photo is listed more than once.");
        }

        var owned = await db.MealImages
            .Where(image => ids.Contains(image.Id) && image.UserId == userId)
            .Select(image => new { image.Id, image.LogEntryId })
            .ToListAsync(cancellationToken);

        foreach (var id in ids)
        {
            var image = owned.FirstOrDefault(candidate => candidate.Id == id);

            if (image is null)
            {
                // Same 400 whether the photo does not exist or belongs to another account: the
                // difference is not the caller's business.
                errors.Add("imageIds", $"There is no photo of yours with the id {id}.");
                continue;
            }

            if (image.LogEntryId is not null && image.LogEntryId != entryId)
            {
                errors.Add("imageIds", $"The photo {id} is already attached to another entry.");
            }
        }
    }

    private static IQueryable<LogEntry> EntriesOf(TrackrDbContext db, Guid userId) =>
        db.LogEntries.AsNoTracking().Where(entry => entry.UserId == userId);

    private static async Task<LogEntryResponse> ReadBackAsync(
        TrackrDbContext db,
        Guid userId,
        Guid entryId,
        CancellationToken cancellationToken)
    {
        var row = await EntriesOf(db, userId)
            .Where(entry => entry.Id == entryId)
            .Select(Projection)
            .FirstAsync(cancellationToken);

        return ToResponse(row);
    }

    /// <summary>
    /// The shape read out of the database, before the nutrient rows become a map.
    /// </summary>
    /// <remarks>
    /// An explicit projection rather than <c>Include</c>, and that is not a style preference:
    /// including <see cref="MealImage"/> would load its <c>bytea</c> column, so every log response
    /// would drag megabytes out of Postgres to render a list that shows none of them. Nothing
    /// outside <c>GET /api/images/{id}</c> may touch that column.
    /// <para>
    /// The dictionary is built afterwards because <c>ToDictionary</c> has no SQL translation.
    /// </para>
    /// </remarks>
    private static System.Linq.Expressions.Expression<Func<LogEntry, EntryRow>> Projection =>
        entry => new EntryRow(
            entry.Id,
            entry.LoggedUtc,
            entry.Note,
            entry.CreatedUtc,
            entry.UpdatedUtc,
            entry.Items
                .OrderBy(item => item.CreatedUtc)
                .ThenBy(item => item.Id)
                .Select(item => new ItemRow(
                    item.Id,
                    item.FoodItemId,
                    item.Name,
                    item.Brand,
                    item.Quantity,
                    item.ServingSize,
                    item.ServingUnit,
                    item.EnergyKcal,
                    item.FatG,
                    item.CarbohydrateG,
                    item.ProteinG,
                    item.Nutrients
                        .Select(nutrient => new AmountRow(nutrient.NutrientKey, nutrient.Amount))
                        .ToList()))
                .ToList(),
            entry.Images
                .OrderBy(image => image.CreatedUtc)
                .Select(image => new MealImageResponse(
                    image.Id,
                    image.ContentType,
                    image.ByteCount,
                    image.CreatedUtc))
                .ToList());

    private static LogEntryResponse ToResponse(EntryRow entry) =>
        new(
            entry.Id,
            entry.LoggedUtc,
            entry.Note,
            entry.Items.Select(item => new LogItemResponse(
                item.Id,
                item.FoodItemId,
                item.Name,
                item.Brand,
                item.Quantity,
                item.ServingSize,
                item.ServingUnit,
                item.EnergyKcal,
                item.FatG,
                item.CarbohydrateG,
                item.ProteinG,
                item.Nutrients.ToDictionary(
                    amount => amount.Key,
                    amount => amount.Amount,
                    StringComparer.Ordinal))).ToArray(),
            entry.Images,
            entry.CreatedUtc,
            entry.UpdatedUtc);

    private sealed record EntryRow(
        Guid Id,
        DateTimeOffset LoggedUtc,
        string? Note,
        DateTimeOffset CreatedUtc,
        DateTimeOffset UpdatedUtc,
        List<ItemRow> Items,
        List<MealImageResponse> Images);

    private sealed record ItemRow(
        Guid Id,
        Guid? FoodItemId,
        string Name,
        string? Brand,
        decimal Quantity,
        decimal? ServingSize,
        string? ServingUnit,
        decimal EnergyKcal,
        decimal FatG,
        decimal CarbohydrateG,
        decimal ProteinG,
        List<AmountRow> Nutrients);

    private sealed record AmountRow(string Key, decimal Amount);
}
