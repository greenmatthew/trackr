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
}
