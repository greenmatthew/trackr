using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Time;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// The food catalog: everything the server knows how to log, personal or shared.
/// </summary>
/// <remarks>
/// There is no pre-loaded database (CLAUDE.md section 10). The catalog accumulates from barcode
/// lookups, from the model's reads of photos and from things typed by hand, which is why these
/// endpoints exist before anything that fills them.
/// <para>
/// <strong>This is an API with no user interface, and it is not meant to acquire one.</strong>
/// "Basic CRUD for catalog and log" is milestone 6's wording and is easy to misread later as
/// licence to build the nutrition data-entry form section 10 forbids. Food is logged by describing
/// it in the chat; these routes are what the chat, the cascade and the stats views call.
/// </para>
/// <para>
/// Two visibilities, chosen at creation. A personal item belongs to one account; a global item
/// (<c>UserId IS NULL</c>) belongs to the household, is visible to every account and may be
/// corrected by any of them. Ownership rules are summarised on each handler; the one worth reading
/// twice is that another account's personal item answers 404 rather than 403, so these routes
/// cannot be used to find out which ids exist.
/// </para>
/// </remarks>
public static class FoodEndpoints
{
    /// <summary>
    /// A hard cap instead of paging. A household's catalog is hundreds of items, not millions, and
    /// paging is machinery to maintain for a scrollbar nobody will reach.
    /// </summary>
    private const int MaxResults = 200;

    public static IEndpointRouteBuilder MapFoodEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/foods", ListFoodsAsync)
            .WithName("ListFoods")
            .WithSummary("Catalog items visible to the caller: their own, plus everything shared.");

        app.MapGet("/api/foods/{id:guid}", GetFoodAsync)
            .WithName("GetFood")
            .WithSummary("One catalog item, with its full nutrient map.");

        app.MapPost("/api/foods", CreateFoodAsync)
            .WithName("CreateFood")
            .WithSummary("Add an item to the catalog. Personal unless the request says otherwise.");

        app.MapPut("/api/foods/{id:guid}", ReplaceFoodAsync)
            .WithName("ReplaceFood")
            .WithSummary("Replace an item, including its whole nutrient map.");

        app.MapPost("/api/foods/{id:guid}/share", ShareFoodAsync)
            .WithName("ShareFood")
            .WithSummary("Promote a personal item to the shared catalog. One-way.");

        app.MapDelete("/api/foods/{id:guid}", DeleteFoodAsync)
            .WithName("DeleteFood")
            .WithSummary("Delete a personal item. Already-logged entries keep their snapshots.");

        return app;
    }

    /// <param name="search">Case-insensitive, matched against name and brand.</param>
    /// <param name="visibility">Narrows to personal or shared items only.</param>
    private static async Task<IResult> ListFoodsAsync(
        string? search,
        FoodVisibility? visibility,
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

        var query = VisibleTo(db, user.Id);

        query = visibility switch
        {
            FoodVisibility.Personal => query.Where(item => item.UserId == user.Id),
            FoodVisibility.Global => query.Where(item => item.UserId == null),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            // ILIKE rather than ToLower().Contains(): it is what Postgres actually does for a
            // case-insensitive match, and the escape character keeps a % somebody typed from
            // silently becoming a wildcard.
            var pattern = $"%{Escape(search.Trim())}%";

            query = query.Where(item =>
                EF.Functions.ILike(item.Name, pattern, "\\")
                || (item.Brand != null && EF.Functions.ILike(item.Brand, pattern, "\\")));
        }

        var items = await query
            .OrderBy(item => item.Name)
            .Take(MaxResults)
            // No Include of the nutrient map: a list of two hundred items would drag several
            // thousand amount rows along for a screen that shows none of them.
            .Select(item => new FoodItemSummaryResponse(
                item.Id,
                item.Name,
                item.Brand,
                item.Barcode,
                item.ServingSize,
                item.ServingUnit,
                item.Source,
                item.UserId == null ? FoodVisibility.Global : FoodVisibility.Personal,
                true,
                item.EnergyKcal,
                item.FatG,
                item.CarbohydrateG,
                item.ProteinG,
                item.UpdatedUtc))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(items);
    }

    private static async Task<IResult> GetFoodAsync(
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

        var item = await VisibleTo(db, user.Id)
            .Include(food => food.Nutrients)
            .FirstOrDefaultAsync(food => food.Id == id, cancellationToken);

        // 404 rather than 403 when the item belongs to somebody else, so this route cannot be used
        // to discover which ids exist - the same instinct as the auth endpoints' single failure
        // message.
        return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> CreateFoodAsync(
        SaveFoodItemRequest request,
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
        var barcode = Validate(request, catalog, errors);

        if (errors.Any)
        {
            return errors.Problem();
        }

        // Null owner means global. Never taken from a user id in the body - the only account these
        // endpoints will act as is the one that authenticated.
        var owner = request.Visibility is FoodVisibility.Global ? (Guid?)null : user.Id;

        if (await BarcodeIsTakenAsync(db, barcode, owner, excluding: null, cancellationToken))
        {
            return BarcodeConflict(owner);
        }

        var now = Timestamps.UtcNow();

        var item = new FoodItem
        {
            UserId = owner,
            Name = request.Name.Trim(),
            Brand = Blank(request.Brand),
            Barcode = barcode,
            // Rounded to what the columns keep, so this response is exactly what a later GET
            // will report - see StoredPrecision.
            ServingSize = StoredPrecision.Measure(request.ServingSize),
            ServingUnit = request.ServingUnit.Trim(),
            Source = request.Source,
            EnergyKcal = StoredPrecision.Amount(request.EnergyKcal),
            FatG = StoredPrecision.Amount(request.FatG),
            CarbohydrateG = StoredPrecision.Amount(request.CarbohydrateG),
            ProteinG = StoredPrecision.Amount(request.ProteinG),
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByUserId = user.Id
        };

        foreach (var (key, amount) in request.Nutrients)
        {
            item.Nutrients.Add(new FoodItemNutrient
            {
                NutrientKey = key,
                Amount = StoredPrecision.Amount(amount)
            });
        }

        db.FoodItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/foods/{item.Id}", ToResponse(item));
    }

    /// <remarks>
    /// A replace rather than a merge, including the nutrient map. A merge would leave "remove a
    /// nutrient that was wrong" with no way to say it.
    /// <para>
    /// A global item may be corrected by any account, wiki-style, which is why every write stamps
    /// <see cref="FoodItem.UpdatedByUserId"/>. What that cannot do is change history: every logged
    /// item carries its own snapshot, so a correction here never rewrites a number somebody has
    /// already confirmed.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReplaceFoodAsync(
        Guid id,
        SaveFoodItemRequest request,
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
        var barcode = Validate(request, catalog, errors);

        if (errors.Any)
        {
            return errors.Problem();
        }

        // Tracked, with the map, because the map is about to be replaced through the change
        // tracker rather than with ExecuteDelete - see below.
        var item = await VisibleTo(db, user.Id)
            .Include(food => food.Nutrients)
            .FirstOrDefaultAsync(food => food.Id == id, cancellationToken);

        if (item is null)
        {
            return Results.NotFound();
        }

        // Visibility is not editable here. Sharing is its own route because it is one-way, and a
        // PUT that silently unshared an item other accounts were already using would be the worst
        // possible way to discover that.
        if (await BarcodeIsTakenAsync(db, barcode, item.UserId, excluding: item.Id, cancellationToken))
        {
            return BarcodeConflict(item.UserId);
        }

        item.Name = request.Name.Trim();
        item.Brand = Blank(request.Brand);
        item.Barcode = barcode;
        item.ServingSize = StoredPrecision.Measure(request.ServingSize);
        item.ServingUnit = request.ServingUnit.Trim();
        item.Source = request.Source;
        item.EnergyKcal = StoredPrecision.Amount(request.EnergyKcal);
        item.FatG = StoredPrecision.Amount(request.FatG);
        item.CarbohydrateG = StoredPrecision.Amount(request.CarbohydrateG);
        item.ProteinG = StoredPrecision.Amount(request.ProteinG);
        item.UpdatedUtc = Timestamps.UtcNow();
        item.UpdatedByUserId = user.Id;

        // RemoveRange and Add inside one SaveChanges, deliberately not ExecuteDeleteAsync: that
        // runs outside the SaveChanges transaction, so a failure on the insert would leave the item
        // with half a nutrient map and no error to explain it.
        db.FoodItemNutrients.RemoveRange(item.Nutrients);
        item.Nutrients.Clear();

        foreach (var (key, amount) in request.Nutrients)
        {
            item.Nutrients.Add(new FoodItemNutrient
            {
                FoodItemId = item.Id,
                NutrientKey = key,
                Amount = StoredPrecision.Amount(amount)
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(item));
    }

    /// <remarks>
    /// One-way on purpose. By the time anyone regrets sharing an item, another account may already
    /// be logging it, so there is no unshare route - the way back is to delete it and let each
    /// account keep its own.
    /// </remarks>
    private static async Task<IResult> ShareFoodAsync(
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

        var item = await VisibleTo(db, user.Id)
            .Include(food => food.Nutrients)
            .FirstOrDefaultAsync(food => food.Id == id, cancellationToken);

        if (item is null)
        {
            return Results.NotFound();
        }

        if (item.UserId is null)
        {
            return Results.Problem(
                title: "Already shared",
                detail: "That item is already in the shared catalog.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (await BarcodeIsTakenAsync(db, item.Barcode, owner: null, excluding: item.Id, cancellationToken))
        {
            return Results.Problem(
                title: "Barcode already shared",
                detail: "The shared catalog already has an item with that barcode. Use that one, or "
                    + "correct it, rather than sharing a second copy.",
                statusCode: StatusCodes.Status409Conflict);
        }

        item.UserId = null;
        item.UpdatedUtc = Timestamps.UtcNow();
        item.UpdatedByUserId = user.Id;

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(ToResponse(item));
    }

    private static async Task<IResult> DeleteFoodAsync(
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

        var item = await VisibleTo(db, user.Id)
            .FirstOrDefaultAsync(food => food.Id == id, cancellationToken);

        if (item is null)
        {
            return Results.NotFound();
        }

        if (item.UserId is null)
        {
            // 403 rather than the 404 an invisible item gets, and that is not a contradiction: a
            // shared item's existence is not a secret, so hiding it here would only be confusing.
            // Removing one is left to a future admin surface; an ordinary account gets a clear no.
            return Results.Problem(
                title: "Shared items cannot be deleted",
                detail: "That item is in the shared catalog, so another account may be relying on "
                    + "it. Correct it instead, or keep a personal item of your own.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        db.FoodItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);

        // Log entries that referenced it keep every number they recorded; only the back-link goes.
        return Results.NoContent();
    }

    /// <summary>Everything this account may see: its own items, plus the shared catalog.</summary>
    private static IQueryable<FoodItem> VisibleTo(TrackrDbContext db, Guid userId) =>
        db.FoodItems.Where(item => item.UserId == userId || item.UserId == null);

    /// <summary>
    /// True when the barcode is already used by another item with the same visibility.
    /// </summary>
    /// <remarks>
    /// Mirrors the two partial unique indexes rather than waiting for one of them to throw, so the
    /// caller gets a 409 with an explanation instead of a 500 with a constraint name. The indexes
    /// remain the thing that actually guarantees it.
    /// </remarks>
    private static async Task<bool> BarcodeIsTakenAsync(
        TrackrDbContext db,
        string? barcode,
        Guid? owner,
        Guid? excluding,
        CancellationToken cancellationToken)
    {
        if (barcode is null)
        {
            return false;
        }

        return await db.FoodItems.AnyAsync(
            item => item.Barcode == barcode
                && item.UserId == owner
                && (excluding == null || item.Id != excluding),
            cancellationToken);
    }

    private static IResult BarcodeConflict(Guid? owner) =>
        Results.Problem(
            title: "Barcode already in use",
            detail: owner is null
                ? "The shared catalog already has an item with that barcode."
                : "You already have an item with that barcode.",
            statusCode: StatusCodes.Status409Conflict);

    private static string? Validate(
        SaveFoodItemRequest request,
        NutrientCatalog catalog,
        ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add("name", "A name is required.");
        }
        else if (request.Name.Trim().Length > 200)
        {
            errors.Add("name", "That name is too long (200 characters at most).");
        }

        if (request.Brand?.Trim().Length > 120)
        {
            errors.Add("brand", "That brand is too long (120 characters at most).");
        }

        if (request.ServingSize <= 0)
        {
            errors.Add("servingSize", "A serving has to be bigger than nothing.");
        }

        if (string.IsNullOrWhiteSpace(request.ServingUnit))
        {
            errors.Add("servingUnit", "A serving unit is required - 'g', 'ml', 'slice', anything.");
        }
        else if (request.ServingUnit.Trim().Length > 32)
        {
            errors.Add("servingUnit", "That serving unit is too long (32 characters at most).");
        }

        NutritionValidation.ValidateCoreNutrients(
            request.EnergyKcal,
            request.FatG,
            request.CarbohydrateG,
            request.ProteinG,
            errors);

        NutritionValidation.ValidateNutrients(request.Nutrients, catalog, "nutrients", errors);

        return NutritionValidation.NormaliseBarcode(request.Barcode, errors);
    }

    /// <summary>
    /// Projects an item, including the nutrient map, which never contains the core four.
    /// </summary>
    /// <remarks>
    /// <c>IsEditable</c> is always true for anything a caller can see, because what is visible is
    /// "mine or global" and what is editable is the same set. It is still sent, so the rule lives
    /// on the server: if a read-only form of sharing ever appears, the app renders the new answer
    /// without shipping a new APK.
    /// </remarks>
    internal static FoodItemResponse ToResponse(FoodItem item) =>
        new(
            item.Id,
            item.Name,
            item.Brand,
            item.Barcode,
            item.ServingSize,
            item.ServingUnit,
            item.Source,
            item.UserId is null ? FoodVisibility.Global : FoodVisibility.Personal,
            true,
            item.EnergyKcal,
            item.FatG,
            item.CarbohydrateG,
            item.ProteinG,
            item.Nutrients.ToDictionary(
                nutrient => nutrient.NutrientKey,
                nutrient => nutrient.Amount,
                StringComparer.Ordinal),
            item.CreatedUtc,
            item.UpdatedUtc,
            item.UpdatedByUserId);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Keeps a % or _ the user typed from behaving as a wildcard.</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
