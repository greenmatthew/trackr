using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Everything one account holds, in one JSON document.
/// </summary>
/// <remarks>
/// CLAUDE.md section 9.13 asks for "exporting the account's data" as part of the self-service
/// milestone 2 left out. The shape is deliberately its own rather than a reuse of the API's
/// response contracts: those exist to be rendered by the app and change as screens do, and an
/// export is read by a person or a script years later. It should describe what is <em>stored</em>.
/// <para>
/// <strong>What it does not carry is meal photographs.</strong> They are the account's data and
/// they are named here - id, type, size, which entry they belong to - but a few hundred meals of
/// base64 would make a document nothing can open, for bytes that are already downloadable one at a
/// time. The manifest says where they are rather than pretending they are not there.
/// </para>
/// <para>
/// Only what belongs to the account. Global catalog items are shared property of the server and
/// are excluded, which is the same rule the delete uses - see <c>DELETE /api/account</c>.
/// </para>
/// </remarks>
public sealed record AccountExport(
    DateTimeOffset ExportedUtc,
    ExportedAccount Account,
    IReadOnlyList<ExportedGoal> Goals,
    IReadOnlyList<ExportedFoodItem> PersonalCatalog,
    IReadOnlyList<ExportedLogEntry> Log,
    IReadOnlyList<ExportedPhoto> Photos)
{
    /// <summary>Builds the whole document. One call per table, and nothing lazy.</summary>
    public static async Task<AccountExport> ForAsync(
        TrackrUser user,
        TrackrDbContext db,
        DateTimeOffset exportedUtc,
        CancellationToken cancellationToken)
    {
        var goals = await db.Goals
            .AsNoTracking()
            .Where(goal => goal.UserId == user.Id)
            .OrderBy(goal => goal.NutrientKey)
            .Select(goal => new ExportedGoal(goal.NutrientKey, goal.Target, goal.Kind))
            .ToListAsync(cancellationToken);

        // Loaded and then mapped, rather than projected in SQL. The nutrient maps are dictionaries
        // and EF cannot translate ToDictionary into a projection - and an export is one document
        // for one account, so materialising it is not the cost it would be on a hot path.
        var items = await db.FoodItems
            .AsNoTracking()
            .Where(item => item.UserId == user.Id)
            .Include(item => item.Nutrients)
            .Include(item => item.Components)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken);

        var catalog = items
            .Select(item => new ExportedFoodItem(
                item.Id,
                item.Name,
                item.Brand,
                item.Barcode,
                item.ServingSize,
                item.ServingUnit,
                item.Source,
                item.EnergyKcal,
                item.FatG,
                item.CarbohydrateG,
                item.ProteinG,
                item.IngredientsText,
                item.Allergens,
                item.DietFlags,
                Amounts(item.Nutrients.Select(n => (n.NutrientKey, n.Amount))),
                [.. item.Components.Select(component =>
                    new ExportedComponent(component.ChildFoodItemId, component.Quantity))],
                item.CreatedUtc,
                item.UpdatedUtc))
            .ToList();

        var entries = await db.LogEntries
            .AsNoTracking()
            .Where(entry => entry.UserId == user.Id)
            .Include(entry => entry.Items)
                .ThenInclude(item => item.Nutrients)
            .OrderBy(entry => entry.LoggedUtc)
            .ToListAsync(cancellationToken);

        var log = entries
            .Select(entry => new ExportedLogEntry(
                entry.Id,
                entry.LoggedUtc,
                entry.Note,
                [.. entry.Items.Select(item => new ExportedLogItem(
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
                    Amounts(item.Nutrients.Select(n => (n.NutrientKey, n.Amount)))))]))
            .ToList();

        // Length rather than content: see the note above about what this document is for. ByteCount
        // is a column, so this query never loads an image.
        var photos = await db.MealImages
            .AsNoTracking()
            .Where(image => image.UserId == user.Id)
            .OrderBy(image => image.CreatedUtc)
            .Select(image => new ExportedPhoto(
                image.Id,
                image.ContentType,
                image.ByteCount,
                image.LogEntryId,
                image.CreatedUtc,
                $"/api/images/{image.Id}"))
            .ToListAsync(cancellationToken);

        return new AccountExport(
            exportedUtc,
            new ExportedAccount(
                user.Id,
                user.Email!,
                user.DisplayName,
                user.TimeZoneId,
                user.EnergyUnit,
                user.MeasurementSystem,
                user.TwoFactorEnabled,
                user.CreatedUtc,
                new BodyMetricsResponse(
                    user.Sex,
                    user.BirthDate,
                    user.HeightCm,
                    user.WeightKg,
                    user.ActivityLevel)),
            goals,
            catalog,
            log,
            photos);
    }

    /// <summary>
    /// A nutrient map, sorted by key.
    /// </summary>
    /// <remarks>
    /// Sorted so that two exports of unchanged data are the same document, which is what makes a
    /// diff between them mean anything. A key absent here was never measured - not zero.
    /// </remarks>
    private static IReadOnlyDictionary<string, decimal> Amounts(
        IEnumerable<(string Key, decimal Amount)> amounts) =>
        amounts
            .OrderBy(amount => amount.Key, StringComparer.Ordinal)
            .ToDictionary(amount => amount.Key, amount => amount.Amount, StringComparer.Ordinal);
}

/// <param name="Body">The optional measurements. Null fields mean nothing was stored.</param>
public sealed record ExportedAccount(
    Guid Id,
    string Email,
    string? DisplayName,
    string? TimeZoneId,
    EnergyUnit EnergyUnit,
    MeasurementSystem MeasurementSystem,
    bool TwoFactorEnabled,
    DateTimeOffset CreatedUtc,
    BodyMetricsResponse Body);

public sealed record ExportedGoal(string NutrientKey, decimal Target, GoalKind Kind);

/// <param name="Nutrients">
/// Keyed as <c>GET /api/nutrients</c> keys them, in the unit that catalog gives. A nutrient absent
/// here was never measured, which is not the same as zero - the rule the whole codebase follows.
/// </param>
/// <param name="Components">
/// The ingredients of a recipe, in servings of each child. Empty for an ordinary item.
/// </param>
public sealed record ExportedFoodItem(
    Guid Id,
    string Name,
    string? Brand,
    string? Barcode,
    decimal ServingSize,
    string ServingUnit,
    FoodSource Source,
    decimal EnergyKcal,
    decimal FatG,
    decimal CarbohydrateG,
    decimal ProteinG,
    string? IngredientsText,
    IReadOnlyList<string>? Allergens,
    IReadOnlyList<string>? DietFlags,
    IReadOnlyDictionary<string, decimal> Nutrients,
    IReadOnlyList<ExportedComponent> Components,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record ExportedComponent(Guid ChildFoodItemId, decimal Quantity);

/// <param name="LoggedUtc">The instant. Which local day that is depends on the zone above.</param>
public sealed record ExportedLogEntry(
    Guid Id,
    DateTimeOffset LoggedUtc,
    string? Note,
    IReadOnlyList<ExportedLogItem> Items);

/// <param name="FoodItemId">
/// The catalog row this came from, or null - either because it was ad hoc, or because that row has
/// since been deleted. The figures below are the snapshot taken when it was logged and do not
/// depend on it.
/// </param>
/// <param name="Quantity">Servings. Every figure here is per serving; multiply to get the total.</param>
public sealed record ExportedLogItem(
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
    IReadOnlyDictionary<string, decimal> Nutrients);

/// <param name="Url">
/// Where the image itself can be fetched while the account still exists. The bytes are deliberately
/// not in this document.
/// </param>
public sealed record ExportedPhoto(
    Guid Id,
    string ContentType,
    int Bytes,
    Guid? LogEntryId,
    DateTimeOffset CreatedUtc,
    string Url);
