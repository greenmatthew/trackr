using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Shared.Health;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Health and readiness routes.
/// </summary>
/// <remarks>
/// Endpoints live in extension methods like this one rather than inline in
/// Program.cs, so that the cascade endpoints added in later milestones have an
/// obvious place to go and Program.cs stays readable.
/// </remarks>
public static class HealthEndpoints
{
    private static readonly string Version =
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        // Routes are written out in full rather than via MapGroup so that the exact
        // URL is obvious at a glance and there is no ambiguity about trailing slashes.

        // AllowAnonymous on every route here is load-bearing, not decorative. Program.cs
        // sets a fallback authorization policy that requires a signed-in user, and these
        // probes have no credentials to offer: the container HEALTHCHECK wgets
        // /api/health/live directly, and `frontend` will not start until `backend` reports
        // healthy - so a 401 here stops the whole stack from coming up.

        // The endpoint the Blazor app calls. Returns the full HealthResponse so the
        // UI can show which dependency is broken rather than just "something failed".
        app.MapGet("/api/health", GetHealthAsync)
            .AllowAnonymous()
            .WithName("Health")
            .WithSummary("Full health report, including database connectivity.");

        // Liveness: no dependencies, so a failure here means the process itself is
        // wedged. This is what the container HEALTHCHECK probes - it must not fail
        // just because Postgres is briefly unavailable, or Docker would restart a
        // perfectly healthy backend.
        app.MapGet("/api/health/live", () => Results.Ok(new { status = "alive" }))
            .AllowAnonymous()
            .WithName("HealthLive")
            .WithSummary("Liveness probe. Always 200 while the process is running.");

        // Readiness (/api/health/ready) is registered by MapHealthChecks in
        // Program.cs so that it picks up AddDbContextCheck.

        return app;
    }

    private static async Task<IResult> GetHealthAsync(
        TrackrDbContext db,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        DependencyStatus database;
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            database = canConnect
                ? new DependencyStatus("PostgreSQL", HealthState.Healthy)
                : new DependencyStatus("PostgreSQL", HealthState.Unhealthy, "Could not open a connection.");
        }
        catch (Exception ex)
        {
            // Surface the reason rather than swallowing it - CLAUDE.md section 5 is
            // emphatic that failures must reach the user, not vanish.
            logger.LogError(ex, "Database health check failed.");
            database = new DependencyStatus("PostgreSQL", HealthState.Unhealthy, ex.Message);
        }

        var response = new HealthResponse(
            Status: database.Status,
            Version: Version,
            TimestampUtc: DateTimeOffset.UtcNow,
            Database: database);

        // 503 when unhealthy so callers that only look at the status code still
        // get the right answer.
        return database.Status is HealthState.Healthy
            ? Results.Ok(response)
            : Results.Json(response, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
