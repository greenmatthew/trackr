using Microsoft.EntityFrameworkCore;

namespace Trackr.Api.Data;

public static class DatabaseStartupExtensions
{
    /// <summary>
    /// Brings the database schema up to date before the app starts serving.
    /// </summary>
    /// <remarks>
    /// Migrations are applied here rather than by a separate `dotnet ef database update`
    /// step because this is a single-instance private deployment redeployed through
    /// Portainer, where "shell into the container and run a CLI command" is exactly the
    /// manual step worth avoiding. There is only ever one backend replica, so there is no
    /// migration race. If that ever changes, this moves to a one-shot init container.
    /// </remarks>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // A short retry loop for the `dotnet watch` case, where Postgres may still be
        // starting up. Note this is deliberately NOT UseNpgsql(o => o.EnableRetryOnFailure):
        // a retrying execution strategy forbids user-initiated transactions, and the
        // invite-redemption path in AuthEndpoints needs BeginTransactionAsync.
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync();
                logger.LogInformation("Database schema is up to date.");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(
                    ex,
                    "Could not migrate the database (attempt {Attempt} of {MaxAttempts}); retrying.",
                    attempt,
                    maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }
    }

    /// <summary>
    /// Brings the <c>Nutrients</c> reference table in line with <see cref="NutrientSeed.All"/>.
    /// </summary>
    /// <remarks>
    /// A startup seeder rather than <c>HasData</c>, for three reasons. wiki/Nutrient-Reference.md
    /// promises that adding selenium later is "a row, not a migration", and with <c>HasData</c> it
    /// is literally a migration file. The seed set will also churn more than the schema does, since
    /// milestones 7 and 8 will meet nutrients this list lacks. And decisively: removing a
    /// <c>HasData</c> row generates <c>DELETE FROM "Nutrients"</c>, which the amount tables'
    /// <c>Restrict</c> foreign key turns into a runtime failure on any database where somebody has
    /// measured that nutrient - a crash in the path that runs before the app serves a request. A
    /// seeder that only inserts and updates cannot produce that failure.
    /// <para>
    /// Insert and update only, never delete. A key in the database that the code no longer defines
    /// is logged rather than removed: it means someone has deployed a downgrade, and their data is
    /// worth more than the tidiness of the table.
    /// </para>
    /// <para>
    /// No concurrency guard, for the same single-replica reason
    /// <see cref="MigrateDatabaseAsync"/> gives above.
    /// </para>
    /// </remarks>
    public static async Task SeedNutrientsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // Tracked, not AsNoTracking: the rows that need correcting are corrected in place.
        var existing = await db.Nutrients.ToDictionaryAsync(
            nutrient => nutrient.Key,
            StringComparer.Ordinal);

        var added = 0;
        var updated = 0;

        foreach (var definition in NutrientSeed.All)
        {
            if (!existing.TryGetValue(definition.Key, out var row))
            {
                // A fresh entity per call. NutrientSeed holds definitions rather than entities
                // precisely so that a shared instance can never be attached to two contexts.
                db.Nutrients.Add(new Nutrient
                {
                    Key = definition.Key,
                    DisplayName = definition.DisplayName,
                    Unit = definition.Unit,
                    Group = definition.Group,
                    SortOrder = definition.SortOrder,
                    IsCore = definition.IsCore
                });

                added++;
                continue;
            }

            if (row.DisplayName == definition.DisplayName
                && row.Unit == definition.Unit
                && row.Group == definition.Group
                && row.SortOrder == definition.SortOrder
                && row.IsCore == definition.IsCore)
            {
                continue;
            }

            row.DisplayName = definition.DisplayName;
            row.Unit = definition.Unit;
            row.Group = definition.Group;
            row.SortOrder = definition.SortOrder;
            row.IsCore = definition.IsCore;

            updated++;
        }

        foreach (var key in existing.Keys.Where(key => !NutrientSeed.All.Any(d => d.Key == key)))
        {
            logger.LogWarning(
                "The database defines nutrient {Key}, which this build does not know about. It has "
                    + "been left alone - amounts recorded against it are still readable - but this "
                    + "usually means an older version has been deployed over a newer one.",
                key);
        }

        if (added > 0 || updated > 0)
        {
            await db.SaveChangesAsync();
        }

        logger.LogInformation(
            "Nutrient catalog ready: {Total} nutrients ({Added} added, {Updated} updated).",
            NutrientSeed.All.Count,
            added,
            updated);
    }
}
