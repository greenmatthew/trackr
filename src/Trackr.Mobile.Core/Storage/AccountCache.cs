using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Core.Storage;

/// <summary>
/// What the app knew about the account last time the server answered.
/// </summary>
/// <remarks>
/// The local store's first real job, and a deliberate one: it arrives before the offline log
/// queue it was really built for (milestone 9), so giving it genuine work now is what keeps it
/// from shipping as empty tables nobody has exercised.
/// <para>
/// Two things it buys. The profile renders with no network, instead of an account screen that
/// says "not signed in" because a train went into a tunnel. And the avatar survives a
/// relaunch, so the ETag the server sends is finally worth something - without somewhere to
/// keep it, every launch re-downloaded the picture in full.
/// </para>
/// <para>
/// <b>Every method degrades rather than throws.</b> This is a cache: an unwritable or corrupt
/// database should cost the app a network request, not a crash. That is the opposite of the
/// rule for the logging cascade (CLAUDE.md section 5), where a swallowed failure could put a
/// wrong number in the database - nothing here is a number anyone reads.
/// </para>
/// </remarks>
public sealed class AccountCache(LocalDatabase database, ILogger<AccountCache> logger)
{
    /// <summary>The account as of the last successful <c>/me</c>, or null if none is stored.</summary>
    public Task<MeResponse?> ReadAccountAsync(CancellationToken cancellationToken = default) =>
        TryAsync<MeResponse?>(null, async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT user_id, email, two_factor, avatar_marker FROM account WHERE id = 1;";

            await using var reader = await command.ExecuteReaderAsync(token);

            if (!await reader.ReadAsync(token))
            {
                return null;
            }

            return new MeResponse(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.IsDBNull(3) ? null : ParseTimestamp(reader.GetString(3)));
        }, cancellationToken);

    public Task WriteAccountAsync(MeResponse user, CancellationToken cancellationToken = default) =>
        TryAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();

            // One row, replaced wholesale. A device holds one signed-in account, so an upsert
            // keyed on anything else would only invite two.
            command.CommandText =
                """
                INSERT INTO account (id, user_id, email, two_factor, avatar_marker, cached_utc)
                VALUES (1, @userId, @email, @twoFactor, @avatarMarker, @cachedUtc)
                ON CONFLICT (id) DO UPDATE SET
                    user_id       = excluded.user_id,
                    email         = excluded.email,
                    two_factor    = excluded.two_factor,
                    avatar_marker = excluded.avatar_marker,
                    cached_utc    = excluded.cached_utc;
                """;

            command.Parameters.AddWithValue("@userId", user.UserId.ToString());
            command.Parameters.AddWithValue("@email", user.Email);
            command.Parameters.AddWithValue("@twoFactor", user.TwoFactorEnabled);
            command.Parameters.AddWithValue(
                "@avatarMarker",
                user.AvatarUpdatedUtc is { } marker ? Format(marker) : DBNull.Value);
            command.Parameters.AddWithValue("@cachedUtc", Format(DateTimeOffset.UtcNow));

            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);

    /// <summary>
    /// The stored picture, if it belongs to <paramref name="userId"/>.
    /// </summary>
    /// <remarks>
    /// Checking the owner as well as the row is belt and braces: signing out clears this, so a
    /// mismatch should be impossible. "Should be impossible" is a poor reason to hand one
    /// account's photograph to another.
    /// </remarks>
    public Task<CachedAvatar?> ReadAvatarAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        TryAsync<CachedAvatar?>(null, async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT content, content_type, etag, marker FROM avatar "
                + "WHERE id = 1 AND user_id = @userId;";
            command.Parameters.AddWithValue("@userId", userId.ToString());

            await using var reader = await command.ExecuteReaderAsync(token);

            if (!await reader.ReadAsync(token))
            {
                return null;
            }

            return new CachedAvatar(
                (byte[])reader.GetValue(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                ParseTimestamp(reader.GetString(3)));
        }, cancellationToken);

    public Task WriteAvatarAsync(
        Guid userId,
        CachedAvatar avatar,
        CancellationToken cancellationToken = default) =>
        TryAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO avatar (id, user_id, content, content_type, etag, marker)
                VALUES (1, @userId, @content, @contentType, @etag, @marker)
                ON CONFLICT (id) DO UPDATE SET
                    user_id      = excluded.user_id,
                    content      = excluded.content,
                    content_type = excluded.content_type,
                    etag         = excluded.etag,
                    marker       = excluded.marker;
                """;

            command.Parameters.AddWithValue("@userId", userId.ToString());
            command.Parameters.AddWithValue("@content", avatar.Content);
            command.Parameters.AddWithValue("@contentType", avatar.ContentType);
            command.Parameters.AddWithValue("@etag", (object?)avatar.ETag ?? DBNull.Value);
            command.Parameters.AddWithValue("@marker", Format(avatar.Marker));

            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);

    public Task ClearAvatarAsync(CancellationToken cancellationToken = default) =>
        TryAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM avatar;";

            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);

    /// <summary>Drops everything about the previous account. Called on sign-out.</summary>
    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        TryAsync(async (connection, token) =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM avatar; DELETE FROM account;";

            await command.ExecuteNonQueryAsync(token);
        }, cancellationToken);

    /// <summary>Round-trippable and sortable, and the same shape the API sends.</summary>
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
        catch (SqliteException ex)
        {
            logger.LogWarning(ex, "The local account cache is unusable; carrying on without it");

            return fallback;
        }
    }

    private Task TryAsync(
        Func<SqliteConnection, CancellationToken, Task> work,
        CancellationToken cancellationToken) =>
        TryAsync(false, async (connection, token) =>
        {
            await work(connection, token);

            return true;
        }, cancellationToken);
}

/// <param name="ETag">
/// The server's tag for these bytes. Null when the picture came from an upload this device
/// made, since the response to a PUT carries the marker but not a tag.
/// </param>
/// <param name="Marker">
/// The account's <c>AvatarUpdatedUtc</c> at the time these bytes were stored, so a launch can
/// tell a current cache from a stale one without downloading anything.
/// </param>
public sealed record CachedAvatar(
    byte[] Content,
    string ContentType,
    string? ETag,
    DateTimeOffset Marker);
