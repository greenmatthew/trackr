using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.Storage;

/// <summary>
/// The last answer to "what have I eaten before", kept so the question can be asked offline.
/// </summary>
/// <remarks>
/// This is what makes the offline queue worth having. Analysing a meal needs the server - the
/// barcode stage, Open Food Facts and the model all live there (CLAUDE.md section 10), and none of
/// it can happen on a phone in a tunnel. Re-logging something already eaten needs nothing but a
/// list, so caching that list is the difference between a queue that catches the rare save which
/// failed mid-flight and one that lets somebody log breakfast on the underground.
/// <para>
/// A cache, so it degrades in the way <see cref="AccountCache"/> does: an unusable database costs
/// the offline list, not the app. What it must not do is pretend - a stale list is shown as a
/// stale list, because the alternative is somebody believing the server answered.
/// </para>
/// </remarks>
public sealed class RecentItemsCache(LocalDatabase database, ILogger<RecentItemsCache> logger)
{
    /// <summary>Replaces the stored list with what the server just returned.</summary>
    public Task WriteAsync(
        Guid userId,
        IReadOnlyList<RecentItemResponse> recent,
        DateTimeOffset cachedUtc,
        CancellationToken cancellationToken = default) =>
        TryAsync(async (connection, token) =>
        {
            await using var transaction = await connection.BeginTransactionAsync(token);

            await using (var clear = connection.CreateCommand())
            {
                // Replaced wholesale rather than merged: the server's order is the answer, and a
                // row that has dropped off the end of it should drop off here too.
                clear.CommandText = "DELETE FROM recent_item WHERE user_id = @userId;";
                clear.Parameters.AddWithValue("@userId", userId.ToString());

                await clear.ExecuteNonQueryAsync(token);
            }

            for (var position = 0; position < recent.Count; position++)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText =
                    "INSERT INTO recent_item (user_id, position, payload, cached_utc) "
                    + "VALUES (@userId, @position, @payload, @cachedUtc);";

                insert.Parameters.AddWithValue("@userId", userId.ToString());
                insert.Parameters.AddWithValue("@position", position);
                insert.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(recent[position]));
                insert.Parameters.AddWithValue("@cachedUtc", Format(cachedUtc));

                await insert.ExecuteNonQueryAsync(token);
            }

            await transaction.CommitAsync(token);

            return true;
        }, cancellationToken);

    /// <summary>
    /// The stored list and when it was stored, or null if this account has none.
    /// </summary>
    /// <remarks>
    /// Null rather than an empty list, and the difference matters on screen: "you have never logged
    /// anything" and "nothing has been cached on this device yet" are different sentences, and only
    /// the first one is about the user.
    /// </remarks>
    public Task<CachedRecents?> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        TryAsync<CachedRecents?>(null, async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT payload, cached_utc FROM recent_item WHERE user_id = @userId ORDER BY position;";
            command.Parameters.AddWithValue("@userId", userId.ToString());

            await using var reader = await command.ExecuteReaderAsync(token);

            var items = new List<RecentItemResponse>();
            DateTimeOffset? cachedUtc = null;

            while (await reader.ReadAsync(token))
            {
                cachedUtc ??= ParseTimestamp(reader.GetString(1));

                if (Deserialize(reader.GetString(0)) is { } item)
                {
                    items.Add(item);
                }
            }

            return cachedUtc is { } stored ? new CachedRecents(items, stored) : null;
        }, cancellationToken);

    /// <summary>Drops the list for an account. Called on sign-out, like the rest of the cache.</summary>
    public Task ClearAsync(Guid userId, CancellationToken cancellationToken = default) =>
        TryAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM recent_item WHERE user_id = @userId;";
            command.Parameters.AddWithValue("@userId", userId.ToString());

            await command.ExecuteNonQueryAsync(token);

            return true;
        }, cancellationToken);

    private static RecentItemResponse? Deserialize(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<RecentItemResponse>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private async Task<T> TryAsync<T>(
        T fallback,
        Func<SqliteConnection, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        try
        {
            return await database.UseAsync(work, cancellationToken);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or NotSupportedException)
        {
            logger.LogWarning(ex, "The offline list of recent foods is unusable; carrying on without it");

            return fallback;
        }
    }

    private Task TryAsync(
        Func<SqliteConnection, CancellationToken, Task<bool>> work,
        CancellationToken cancellationToken) =>
        TryAsync(false, work, cancellationToken);
}

/// <param name="CachedUtc">When the server last answered with this list.</param>
public sealed record CachedRecents(IReadOnlyList<RecentItemResponse> Items, DateTimeOffset CachedUtc);
