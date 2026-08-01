using System.Text.Json.Serialization;

namespace Trackr.Shared.Health;

/// <summary>
/// Result of <c>GET /api/health</c>. Deliberately simple: this is the milestone 1
/// probe that proves the browser can reach the API and the API can reach Postgres.
/// </summary>
/// <param name="Status">Overall status of the API and its dependencies.</param>
/// <param name="Version">Informational version of the running API build.</param>
/// <param name="TimestampUtc">When the API handled the request.</param>
/// <param name="Database">Whether EF Core could open a connection to Postgres.</param>
public sealed record HealthResponse(
    HealthState Status,
    string Version,
    DateTimeOffset TimestampUtc,
    DependencyStatus Database);

/// <summary>Status of a single dependency the API relies on.</summary>
/// <param name="Name">Human-readable dependency name, e.g. "PostgreSQL".</param>
/// <param name="Status">Whether the dependency responded.</param>
/// <param name="Detail">Error text when unhealthy; null when healthy.</param>
public sealed record DependencyStatus(
    string Name,
    HealthState Status,
    string? Detail = null);

/// <remarks>
/// Serialised as a string rather than an integer. The converter is declared on the
/// type so both the API and the client agree without either side configuring it.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<HealthState>))]
public enum HealthState
{
    Healthy,
    Degraded,
    Unhealthy
}
