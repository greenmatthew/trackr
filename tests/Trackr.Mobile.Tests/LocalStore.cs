using Microsoft.Extensions.Logging.Abstractions;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.Storage;

namespace Trackr.Mobile.Tests;

/// <summary>
/// A real SQLite store, in memory.
/// </summary>
/// <remarks>
/// Not a substitute. The store is in <c>Trackr.Mobile.Core</c> precisely so the actual SQL -
/// the schema, the upserts, the round-tripping of a <c>DateTimeOffset</c> through TEXT - runs
/// in these tests rather than only on a device. A mock would prove nothing about any of it.
/// <para>
/// Each instance gets its own private database, because <c>LocalDatabase</c> holds one
/// connection open and an unshared <c>:memory:</c> database lives exactly as long as that
/// connection does. That is per-test isolation for free.
/// </para>
/// </remarks>
internal static class LocalStore
{
    public static AccountCache InMemory() =>
        new(Database(), NullLogger<AccountCache>.Instance);

    /// <summary>The offline queue, on its own database.</summary>
    public static LogOutbox Outbox() =>
        new(Database(), NullLogger<LogOutbox>.Instance);

    /// <summary>The offline list of foods logged before, on its own database.</summary>
    public static RecentItemsCache Recents() =>
        new(Database(), NullLogger<RecentItemsCache>.Instance);

    private static LocalDatabase Database() =>
        new(new MemoryStorePath(), NullLogger<LocalDatabase>.Instance);

    private sealed class MemoryStorePath : ILocalStorePath
    {
        public string DataSource => ":memory:";
    }
}
