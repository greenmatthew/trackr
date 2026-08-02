using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Core.Storage;

/// <summary>
/// The phone's own database: connection, schema and the lock around both.
/// </summary>
/// <remarks>
/// Hand-written SQL rather than EF Core, which the server uses. The schema here is a handful
/// of tables the app owns end to end, and an ORM would buy nothing while costing a
/// reflection-heavy dependency for the Android linker to trim badly.
/// <para>
/// <b>One connection, held open for the life of the process.</b> SQLite serialises writers
/// regardless, so a pool would win nothing; keeping a single connection also means the tests
/// can point <see cref="ILocalStorePath"/> at <c>:memory:</c> and have the database survive
/// between calls, which is what makes the real store testable without a device.
/// </para>
/// <para>
/// Callers reach the connection through <see cref="UseAsync{T}"/>, which holds a semaphore -
/// <c>SqliteConnection</c> is not thread-safe, and view models can easily touch this from
/// several screens at once on resume.
/// </para>
/// </remarks>
public sealed class LocalDatabase(ILocalStorePath storePath, ILogger<LocalDatabase> logger)
    : IAsyncDisposable
{
    /// <summary>
    /// The schema, one entry per version. Never edit an entry that has shipped - append.
    /// </summary>
    /// <remarks>
    /// The array index plus one is the version, recorded in <c>PRAGMA user_version</c>. That
    /// pragma is SQLite's own four-byte slot in the file header, so the schema version needs
    /// no table of its own and cannot get out of step with the file it describes.
    /// </remarks>
    private static readonly string[] Migrations =
    [
        // 1: what the app knew about the account at the last successful sign-in, so the
        // profile and the avatar render before - or without - a reply from the server.
        """
        CREATE TABLE account (
            id            INTEGER PRIMARY KEY CHECK (id = 1),
            user_id       TEXT    NOT NULL,
            email         TEXT    NOT NULL,
            two_factor    INTEGER NOT NULL,
            avatar_marker TEXT    NULL,
            cached_utc    TEXT    NOT NULL
        );

        CREATE TABLE avatar (
            id           INTEGER PRIMARY KEY CHECK (id = 1),
            user_id      TEXT    NOT NULL,
            content      BLOB    NOT NULL,
            content_type TEXT    NOT NULL,
            etag         TEXT    NULL,
            marker       TEXT    NOT NULL
        );
        """,
    ];

    private readonly SemaphoreSlim _gate = new(1, 1);

    private SqliteConnection? _connection;

    /// <summary>
    /// Runs <paramref name="work"/> against the open, migrated database, one caller at a time.
    /// </summary>
    public async Task<T> UseAsync<T>(
        Func<SqliteConnection, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await work(await OpenAsync(cancellationToken), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = storePath.DataSource }.ToString());

        await connection.OpenAsync(cancellationToken);

        await MigrateAsync(connection, cancellationToken);

        _connection = connection;

        return connection;
    }

    private async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var version = Convert.ToInt32(await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken));

        if (version >= Migrations.Length)
        {
            return;
        }

        logger.LogInformation(
            "Migrating the local database from version {From} to {To}",
            version,
            Migrations.Length);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        for (var next = version; next < Migrations.Length; next++)
        {
            await using var step = connection.CreateCommand();
            step.CommandText = Migrations[next];
            await step.ExecuteNonQueryAsync(cancellationToken);
        }

        // Interpolated because PRAGMA does not take parameters. The value is the length of a
        // static array in this file, so there is nothing here a caller could influence.
        await using var stamp = connection.CreateCommand();
        stamp.CommandText = $"PRAGMA user_version = {Migrations.Length};";
        await stamp.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return await command.ExecuteScalarAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _gate.Dispose();
    }
}
