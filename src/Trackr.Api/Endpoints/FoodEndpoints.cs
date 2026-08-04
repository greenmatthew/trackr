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

    /// <summary>
    /// A sanity limit on one recipe's ingredient list, not a considered ceiling on cooking.
    /// </summary>
    /// <remarks>
    /// Nesting is how a big recipe is expressed - a sauce is one ingredient of the dish - so a flat
    /// list this long is far more likely to be a client looping than a person cooking.
    /// </remarks>
    private const int MaxComponents = 50;

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
                item.UpdatedUtc,
                // One column rather than the ingredient list: non-null is what marks a recipe, and a
                // list showing two hundred items has no use for what any of them are made of.
                item.Yield))
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

        var item = await WithDetail(VisibleTo(db, user.Id))
            .FirstOrDefaultAsync(food => food.Id == id, cancellationToken);

        // 404 rather than 403 when the item belongs to somebody else, so this route cannot be used
        // to discover which ids exist - the same instinct as the auth endpoints' single failure
        // message.
        return item is null ? Results.NotFound() : Results.Ok(ToResponse(item));
    }

    /// <remarks>
    /// A recipe is created the same way as anything else - it is still a <see cref="FoodItem"/> -
    /// but its nutrition comes from its ingredients rather than from the request. No cycle check is
    /// needed here: nothing can already contain an item that does not exist yet.
    /// </remarks>
    private static async Task<IResult> CreateFoodAsync(
        SaveFoodItemRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        NutrientCatalog catalog,
        CompositeNutrition composites,
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
        var isRecipe = request.Components.Count > 0;

        // Only recipes need the transaction, and they need it for the ingredients rather than for
        // themselves: without it an ingredient could be deleted between being resolved and being
        // referenced, turning a clean 409 elsewhere into a foreign-key error here.
        await using var transaction = isRecipe
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (transaction is not null)
        {
            await composites.TakeWriteLockAsync(cancellationToken);
        }

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
            CreatedUtc = now,
            UpdatedUtc = now,
            UpdatedByUserId = user.Id
        };

        if (isRecipe)
        {
            var ingredients = await ResolveComponentsAsync(
                db,
                request.Components,
                user.Id,
                recipeIsGlobal: owner is null,
                errors,
                cancellationToken);

            if (errors.Any)
            {
                return errors.Problem();
            }

            item.Yield = StoredPrecision.Measure(request.Yield!.Value);

            AttachComponents(item, request.Components, ingredients, db);
        }
        else
        {
            ApplyNutritionFrom(item, request);
        }

        db.FoodItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Results.Created($"/api/foods/{item.Id}", ToResponse(item));
    }

    /// <remarks>
    /// A replace rather than a merge, including the nutrient map and the ingredient list. A merge
    /// would leave "remove a nutrient that was wrong" with no way to say it.
    /// <para>
    /// A global item may be corrected by any account, wiki-style, which is why every write stamps
    /// <see cref="FoodItem.UpdatedByUserId"/>. What that cannot do is change history: every logged
    /// item carries its own snapshot, so a correction here never rewrites a number somebody has
    /// already confirmed.
    /// </para>
    /// <para>
    /// <strong>Recipes above this item are a different matter, and are recomputed here.</strong>
    /// Their numbers are a cache of their ingredients', so correcting an ingredient has to push
    /// upward or the recipe reports the old figure forever. That runs in this request's transaction
    /// and across account boundaries - a shared ingredient may be in several people's recipes, and
    /// fixing only the editor's would leave the rest quietly wrong.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ReplaceFoodAsync(
        Guid id,
        SaveFoodItemRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        NutrientCatalog catalog,
        CompositeNutrition composites,
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

        // Every replace takes the transaction and the lock, not only the ones that touch a recipe:
        // any item may be an ingredient, so any edit may have to fan out, and the fan-out has to
        // commit or fail with the edit that caused it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await composites.TakeWriteLockAsync(cancellationToken);

        // Tracked, with the map and the ingredient list, because both are about to be replaced
        // through the change tracker rather than with ExecuteDelete - see below.
        var item = await VisibleTo(db, user.Id)
            .Include(food => food.Nutrients)
            .Include(food => food.Components)
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

        var isRecipe = request.Components.Count > 0;
        Dictionary<Guid, FoodItem> ingredients = [];

        if (isRecipe)
        {
            ingredients = await ResolveComponentsAsync(
                db,
                request.Components,
                user.Id,
                recipeIsGlobal: item.UserId is null,
                errors,
                cancellationToken);

            if (!errors.Any
                && await composites.WouldFormACycleAsync(
                    item.Id,
                    [.. request.Components.Select(component => component.FoodItemId)],
                    cancellationToken))
            {
                errors.Add(
                    "components",
                    "That would make the recipe an ingredient of itself, directly or through "
                        + "another recipe.");
            }

            if (errors.Any)
            {
                return errors.Problem();
            }
        }

        item.Name = request.Name.Trim();
        item.Brand = Blank(request.Brand);
        item.Barcode = barcode;
        item.ServingSize = StoredPrecision.Measure(request.ServingSize);
        item.ServingUnit = request.ServingUnit.Trim();
        item.Source = request.Source;

        var now = Timestamps.UtcNow();

        item.UpdatedUtc = now;
        item.UpdatedByUserId = user.Id;

        // The ingredient list goes wholesale, exactly like the nutrient map. An item may also stop
        // being a recipe here, or start being one - both are ordinary corrections.
        db.FoodItemComponents.RemoveRange(item.Components);
        item.Components.Clear();

        if (isRecipe)
        {
            item.Yield = StoredPrecision.Measure(request.Yield!.Value);

            AttachComponents(item, request.Components, ingredients, db);
        }
        else
        {
            item.Yield = null;

            ApplyNutritionFrom(item, request);
        }

        await db.SaveChangesAsync(cancellationToken);

        // After the save, so the walk reads the numbers that were just written rather than the ones
        // sitting in the change tracker.
        await composites.RecomputeAncestorsAsync(item.Id, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(ToResponse(item));
    }

    /// <remarks>
    /// One-way on purpose. By the time anyone regrets sharing an item, another account may already
    /// be logging it, so there is no unshare route - the way back is to delete it and let each
    /// account keep its own.
    /// <para>
    /// A recipe can only be shared once all of its ingredients are. Otherwise the household would
    /// see a recipe whose ingredient list it cannot open, and the private item underneath it would
    /// vanish - taking the recipe's numbers with it - the day its owner deleted their account.
    /// </para>
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

        var item = await WithDetail(VisibleTo(db, user.Id))
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

        var personal = item.Components
            .Where(component => component.Child!.UserId is not null)
            .Select(component => component.Child!.Name)
            .ToArray();

        if (personal.Length > 0)
        {
            return Results.Problem(
                title: "Its ingredients are not shared",
                detail: $"Share these first, then share the recipe: {string.Join(", ", personal)}.",
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

    /// <remarks>
    /// Deleting an item a recipe is made of is refused rather than cascaded. The database would
    /// cascade happily and leave the recipe reporting numbers it can no longer justify, which is the
    /// silent wrong number CLAUDE.md section 2 is written against; recomputing the recipe without
    /// the ingredient would be worse still, because the answer would look just as confident.
    /// </remarks>
    private static async Task<IResult> DeleteFoodAsync(
        Guid id,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        CompositeNutrition composites,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // The lock, so a recipe cannot pick this item up between the check below and the delete.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await composites.TakeWriteLockAsync(cancellationToken);

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

        var recipes = await composites.RecipesUsingAsync(item.Id, cancellationToken);

        if (recipes.Count > 0)
        {
            return Results.Problem(
                title: "It is an ingredient",
                detail: $"Remove it from these first: {string.Join(", ", recipes)}.",
                statusCode: StatusCodes.Status409Conflict);
        }

        db.FoodItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Log entries that referenced it keep every number they recorded; only the back-link goes.
        return Results.NoContent();
    }

    /// <summary>Everything this account may see: its own items, plus the shared catalog.</summary>
    private static IQueryable<FoodItem> VisibleTo(TrackrDbContext db, Guid userId) =>
        db.FoodItems.Where(item => item.UserId == userId || item.UserId == null);

    /// <summary>
    /// One item with everything <see cref="ToResponse"/> needs: the nutrient map, and - for a recipe
    /// - its ingredients and their names.
    /// </summary>
    /// <remarks>
    /// One level only. A recipe made of recipes still shows its own ingredient list, and opening one
    /// of those is another GET, because a client that wanted the whole tree would be rendering a
    /// screen nobody has asked for.
    /// </remarks>
    private static IQueryable<FoodItem> WithDetail(IQueryable<FoodItem> items) =>
        items
            .Include(item => item.Nutrients)
            .Include(item => item.Components)
            .ThenInclude(component => component.Child);

    /// <summary>Copies the request's own nutrition onto an item that is not a recipe.</summary>
    /// <remarks>
    /// RemoveRange and Add inside one SaveChanges, deliberately not ExecuteDeleteAsync: that runs
    /// outside the SaveChanges transaction, so a failure on the insert would leave the item with
    /// half a nutrient map and no error to explain it.
    /// </remarks>
    private static void ApplyNutritionFrom(FoodItem item, SaveFoodItemRequest request)
    {
        item.EnergyKcal = StoredPrecision.Amount(request.EnergyKcal);
        item.FatG = StoredPrecision.Amount(request.FatG);
        item.CarbohydrateG = StoredPrecision.Amount(request.CarbohydrateG);
        item.ProteinG = StoredPrecision.Amount(request.ProteinG);

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
    }

    /// <summary>
    /// Hangs the ingredient list on a recipe and computes the nutrition it implies.
    /// </summary>
    /// <remarks>
    /// The nutrient values in the request are ignored rather than rejected, so that fetching an
    /// item, renaming it and sending the whole thing back does what it looks like it does.
    /// </remarks>
    private static void AttachComponents(
        FoodItem item,
        IReadOnlyCollection<SaveFoodComponentRequest> components,
        IReadOnlyDictionary<Guid, FoodItem> ingredients,
        TrackrDbContext db)
    {
        var parts = new List<(FoodItem Child, decimal Quantity)>(components.Count);

        foreach (var component in components)
        {
            var child = ingredients[component.FoodItemId];
            var quantity = StoredPrecision.Measure(component.Quantity);

            item.Components.Add(new FoodItemComponent
            {
                ParentFoodItemId = item.Id,
                ChildFoodItemId = child.Id,
                // Set so the response can name the ingredient without a reload.
                Child = child,
                Quantity = quantity
            });

            parts.Add((child, quantity));
        }

        CompositeNutrition.Materialise(item, parts, db);
    }

    /// <summary>
    /// Loads the ingredients a request names, complaining about any this account cannot use.
    /// </summary>
    /// <remarks>
    /// An unknown id and somebody else's personal item give the same message, for the reason
    /// <c>GET</c> answers 404 rather than 403: these routes must not report which ids exist.
    /// </remarks>
    private static async Task<Dictionary<Guid, FoodItem>> ResolveComponentsAsync(
        TrackrDbContext db,
        IReadOnlyCollection<SaveFoodComponentRequest> components,
        Guid callerId,
        bool recipeIsGlobal,
        ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        var ids = components.Select(component => component.FoodItemId).ToArray();

        // The nutrient maps come along because the recipe's own numbers are about to be computed
        // from them.
        var found = await VisibleTo(db, callerId)
            .Include(item => item.Nutrients)
            .Where(item => ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        foreach (var id in ids.Where(id => !found.ContainsKey(id)))
        {
            errors.Add("components", $"'{id}' is not an item this account can use as an ingredient.");
        }

        if (recipeIsGlobal)
        {
            foreach (var name in found.Values
                .Where(item => item.UserId is not null)
                .Select(item => item.Name))
            {
                errors.Add(
                    "components",
                    $"'{name}' is personal, so it cannot be an ingredient of a shared recipe. Share "
                        + "it first.");
            }
        }

        return found;
    }

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

        ValidateRecipeShape(request, errors);

        // A recipe's nutrition is computed, so the values in the request are not its own and are
        // neither validated nor stored. Everything else has to carry its own numbers.
        if (request.Components.Count == 0)
        {
            NutritionValidation.ValidateCoreNutrients(
                request.EnergyKcal,
                request.FatG,
                request.CarbohydrateG,
                request.ProteinG,
                errors);

            NutritionValidation.ValidateNutrients(request.Nutrients, catalog, "nutrients", errors);
        }

        return NutritionValidation.NormaliseBarcode(request.Barcode, errors);
    }

    /// <summary>
    /// The checks on a recipe that need nothing but the request: that it is one consistently, and
    /// that its ingredient list is a list.
    /// </summary>
    private static void ValidateRecipeShape(SaveFoodItemRequest request, ValidationErrors errors)
    {
        // The two go together, in both directions. A yield with nothing to divide would be a number
        // with no meaning, and ingredients with no yield leave "per serving" undefined - which is
        // the one thing every other item on the server is guaranteed to have.
        if (request.Components.Count == 0)
        {
            if (request.Yield is not null)
            {
                errors.Add("yield", "A yield only means something with ingredients to divide.");
            }

            return;
        }

        if (request.Yield is null)
        {
            errors.Add(
                "yield",
                "A recipe needs a yield - how many servings one batch makes - so its nutrition can "
                    + "be expressed per serving like everything else.");
        }
        else if (request.Yield <= 0)
        {
            errors.Add("yield", "A batch has to make more than nothing.");
        }

        if (request.Components.Count > MaxComponents)
        {
            errors.Add(
                "components",
                $"That is more than {MaxComponents} ingredients. Break the recipe into parts and use "
                    + "one as an ingredient of the other.");
        }

        if (request.Barcode is not null)
        {
            errors.Add(
                "barcode",
                "A recipe has no barcode - a barcode identifies one manufacturer's product, and the "
                    + "catalog treats it as unique.");
        }

        foreach (var component in request.Components.Where(component => component.Quantity <= 0))
        {
            errors.Add(
                "components",
                $"'{component.FoodItemId}' has a quantity of {component.Quantity}. An ingredient "
                    + "that is not in the recipe should be left out of it.");
        }

        foreach (var id in request.Components
            .GroupBy(component => component.FoodItemId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key))
        {
            errors.Add(
                "components",
                $"'{id}' is listed more than once. Add the quantities up and send it once.");
        }
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
            item.UpdatedByUserId,
            item.Yield,
            [.. item.Components
                .Where(component => component.Child is not null)
                .OrderBy(component => component.Child!.Name, StringComparer.Ordinal)
                .Select(component => new FoodComponentResponse(
                    component.ChildFoodItemId,
                    component.Child!.Name,
                    component.Child.Brand,
                    component.Quantity,
                    component.Child.ServingSize,
                    component.Child.ServingUnit))]);

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Keeps a % or _ the user typed from behaving as a wildcard.</summary>
    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
