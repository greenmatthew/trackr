using Microsoft.EntityFrameworkCore;

namespace Trackr.Api.Data;

/// <summary>
/// The application's EF Core context.
/// </summary>
/// <remarks>
/// Intentionally empty for milestone 1. There are no entities and no migrations
/// yet - the food catalog, log entries and the extensible nutrient store arrive in
/// milestone 3 (see CLAUDE.md section 7). Right now this exists so the health check
/// can prove that EF Core is configured and can open a connection to Postgres.
/// </remarks>
public class TrackrDbContext(DbContextOptions<TrackrDbContext> options) : DbContext(options)
{
}
