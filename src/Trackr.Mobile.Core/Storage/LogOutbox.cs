using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Trackr.Shared.Nutrition;

namespace Trackr.Mobile.Core.Storage;

/// <summary>
/// Meals the user approved and the server has not taken yet.
/// </summary>
/// <remarks>
/// The phone's half of milestone 14's offline queue, and the one piece of local storage that is
/// <b>not</b> a cache. <see cref="AccountCache"/> degrades on purpose - losing it costs a network
/// request. Losing a row here would silently discard food somebody confirmed, which is the failure
/// CLAUDE.md section 2 puts first arriving by a new route, so every method here reports whether it
/// worked and the caller is expected to care.
/// <para>
/// What is stored is the <see cref="SaveLogEntryRequest"/> as JSON: exactly the bytes that would
/// have been posted. Taking it apart into columns would create a second place for a number to
/// change between what was approved and what is stored, which is the thing milestone 9 went out of
/// its way to avoid on the way in.
/// </para>
/// <para>
/// Rows carry the account that queued them and are only ever read back for that account. A phone
/// signs out and back in, and a meal is not something to hand to whoever holds it next.
/// </para>
/// </remarks>
public sealed class LogOutbox(LocalDatabase database, ILogger<LogOutbox> logger)
{
    /// <summary>
    /// Adds a confirmed meal to the queue. False means it was not stored and nothing was written.
    /// </summary>
    /// <remarks>
    /// The request must already carry its <see cref="SaveLogEntryRequest.ClientRequestId"/>: it is
    /// this table's primary key and the server's de-duplication key, and inventing one here would
    /// give a retry a different key from the attempt it is retrying.
    /// </remarks>
    public async Task<bool> EnqueueAsync(
        Guid userId,
        SaveLogEntryRequest request,
        string summary,
        DateTimeOffset queuedUtc,
        CancellationToken cancellationToken = default)
    {
        if (request.ClientRequestId is not { } id)
        {
            throw new ArgumentException(
                "A queued save must carry a client request id.",
                nameof(request));
        }

        try
        {
            return await database.UseAsync(async (connection, token) =>
            {
                await using var command = connection.CreateCommand();

                // DO NOTHING rather than an update: the row already here is the same meal, and
                // rewriting it would reset the attempt count that tells the user why it is stuck.
                command.CommandText =
                    """
                    INSERT INTO outbox (
                        client_request_id, user_id, payload, summary, queued_utc)
                    VALUES (@id, @userId, @payload, @summary, @queuedUtc)
                    ON CONFLICT (client_request_id) DO NOTHING;
                    """;

                command.Parameters.AddWithValue("@id", id.ToString());
                command.Parameters.AddWithValue("@userId", userId.ToString());
                command.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(request));
                command.Parameters.AddWithValue("@summary", summary);
                command.Parameters.AddWithValue("@queuedUtc", Format(queuedUtc));

                await command.ExecuteNonQueryAsync(token);

                return true;
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or NotSupportedException)
        {
            logger.LogError(ex, "A confirmed meal could not be queued");

            return false;
        }
    }

    /// <summary>
    /// Everything queued for this account, oldest first. Rejected rows included.
    /// </summary>
    /// <remarks>
    /// Oldest first because that is the order they were eaten in, and because a queue that sent its
    /// newest first would leave the oldest to be the one that never goes.
    /// </remarks>
    public Task<IReadOnlyList<QueuedMeal>> ReadAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        TryAsync<IReadOnlyList<QueuedMeal>>([], async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT client_request_id, payload, summary, queued_utc, attempts, last_error, "
                + "rejected FROM outbox WHERE user_id = @userId ORDER BY queued_utc, rowid;";
            command.Parameters.AddWithValue("@userId", userId.ToString());

            await using var reader = await command.ExecuteReaderAsync(token);

            var queued = new List<QueuedMeal>();

            while (await reader.ReadAsync(token))
            {
                var request = Deserialize(reader.GetString(1));

                if (request is null)
                {
                    // Unreadable JSON cannot be posted and cannot be shown, and leaving it would
                    // make the queue permanently non-empty for a meal nobody can see.
                    logger.LogError(
                        "Dropping queued meal {ClientRequestId}: its payload could not be read",
                        reader.GetString(0));

                    continue;
                }

                queued.Add(new QueuedMeal(
                    Guid.Parse(reader.GetString(0)),
                    request,
                    reader.GetString(2),
                    ParseTimestamp(reader.GetString(3)),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.GetBoolean(6)));
            }

            return queued;
        }, cancellationToken);

    /// <summary>Drops a row, because the server has it. Also how a rejected meal is discarded.</summary>
    public Task<bool> RemoveAsync(Guid clientRequestId, CancellationToken cancellationToken = default) =>
        TryAsync(false, async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM outbox WHERE client_request_id = @id;";
            command.Parameters.AddWithValue("@id", clientRequestId.ToString());

            return await command.ExecuteNonQueryAsync(token) > 0;
        }, cancellationToken);

    /// <summary>
    /// Records an attempt that did not store the meal.
    /// </summary>
    /// <param name="rejected">
    /// True when the server answered and refused. A rejected row is never sent again on its own -
    /// retrying a 400 forever is how a queue turns into a loop - so it waits to be shown to the
    /// user, who is the only one who can do anything about it.
    /// </param>
    public Task<bool> RecordAttemptAsync(
        Guid clientRequestId,
        string? problem,
        bool rejected,
        DateTimeOffset attemptedUtc,
        CancellationToken cancellationToken = default) =>
        TryAsync(false, async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE outbox
                SET attempts         = attempts + 1,
                    last_attempt_utc = @attemptedUtc,
                    last_error       = @problem,
                    rejected         = @rejected
                WHERE client_request_id = @id;
                """;

            command.Parameters.AddWithValue("@id", clientRequestId.ToString());
            command.Parameters.AddWithValue("@attemptedUtc", Format(attemptedUtc));
            command.Parameters.AddWithValue("@problem", (object?)problem ?? DBNull.Value);
            command.Parameters.AddWithValue("@rejected", rejected);

            return await command.ExecuteNonQueryAsync(token) > 0;
        }, cancellationToken);

    private static SaveLogEntryRequest? Deserialize(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<SaveLogEntryRequest>(payload);
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

    /// <remarks>
    /// The reads and the bookkeeping degrade to a fallback, unlike <see cref="EnqueueAsync"/>: by
    /// the time a row exists the meal is safe, and an unreadable database at that point should cost
    /// a later delivery rather than a crash on a screen nobody was worried about.
    /// </remarks>
    private async Task<T> TryAsync<T>(
        T fallback,
        Func<SqliteConnection, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        try
        {
            return await database.UseAsync(work, cancellationToken);
        }
        catch (SqliteException ex)
        {
            logger.LogError(ex, "The offline queue could not be read or updated");

            return fallback;
        }
    }
}

/// <summary>One meal waiting to reach the server.</summary>
/// <param name="Id">The client request id: this row's key, and the server's de-duplication key.</param>
/// <param name="Summary">What to call it on screen - the first item's name, and how many followed.</param>
/// <param name="Rejected">
/// The server answered and refused. Nothing will retry it; the user decides whether to discard it.
/// </param>
public sealed record QueuedMeal(
    Guid Id,
    SaveLogEntryRequest Request,
    string Summary,
    DateTimeOffset QueuedUtc,
    int Attempts,
    string? LastError,
    bool Rejected);
