using Testcontainers.PostgreSql;
using Xunit;

namespace Trackr.Api.Tests.Infrastructure;

/// <summary>
/// A throwaway Postgres container shared by the whole test assembly.
/// </summary>
/// <remarks>
/// A real database rather than a fake, deliberately. EF's InMemory provider cannot apply
/// migrations at all, and SQLite would force EnsureCreated - so in either case the
/// migrations, the unique indexes and the uuid/timestamptz mappings would never be
/// exercised, which is most of what is worth testing here. The cost is that `dotnet test`
/// needs Docker.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("trackr_test")
        .WithUsername("trackr")
        .WithPassword("trackr_test")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// Puts every test class in one collection so they share the single container above, and
/// so they do not run in parallel against the same database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
