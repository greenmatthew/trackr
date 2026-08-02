using Trackr.Shared.Auth;

namespace Trackr.Mobile.Tests;

/// <summary>
/// The local store, exercised as real SQLite rather than as a substitute.
/// </summary>
/// <remarks>
/// The schema, the upserts and the round-trip of a <see cref="DateTimeOffset"/> through a TEXT
/// column are exactly the parts that would otherwise only be discovered on a phone. This is
/// the whole reason the store sits in <c>Trackr.Mobile.Core</c> - see <see cref="LocalStore"/>.
/// </remarks>
public sealed class AccountCacheTests
{
    private static readonly MeResponse Owner = new(
        Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"),
        "owner@example.test",
        TwoFactorEnabled: true,
        new DateTimeOffset(2026, 8, 2, 15, 9, 42, 426, TimeSpan.Zero));

    [Fact]
    public async Task An_empty_store_migrates_itself_and_answers_nothing()
    {
        var cache = LocalStore.InMemory();

        // Reading first, before any write: this is the launch path, and it is also what
        // creates the schema. A missing row must not look like a broken database.
        Assert.Null(await cache.ReadAccountAsync());
        Assert.Null(await cache.ReadAvatarAsync(Owner.UserId));
    }

    [Fact]
    public async Task The_account_survives_a_round_trip_intact()
    {
        var cache = LocalStore.InMemory();

        await cache.WriteAccountAsync(Owner);

        var read = await cache.ReadAccountAsync();

        Assert.Equal(Owner, read);

        // Named separately because the marker is the field a sloppy TEXT round trip would
        // quietly damage, and it is the one the whole avatar cache turns on.
        Assert.Equal(Owner.AvatarUpdatedUtc, read?.AvatarUpdatedUtc);
    }

    [Fact]
    public async Task Writing_again_replaces_rather_than_accumulates()
    {
        var cache = LocalStore.InMemory();

        await cache.WriteAccountAsync(Owner);
        await cache.WriteAccountAsync(Owner with { Email = "renamed@example.test" });

        // A device holds one signed-in account. If this ever returned the first write, the
        // upsert has stopped being an upsert.
        Assert.Equal("renamed@example.test", (await cache.ReadAccountAsync())?.Email);
    }

    [Fact]
    public async Task A_null_marker_is_stored_as_null_rather_than_as_a_date()
    {
        var cache = LocalStore.InMemory();

        await cache.WriteAccountAsync(Owner with { AvatarUpdatedUtc = null });

        Assert.Null((await cache.ReadAccountAsync())?.AvatarUpdatedUtc);
    }

    [Fact]
    public async Task The_picture_survives_a_round_trip_intact()
    {
        var cache = LocalStore.InMemory();
        var avatar = new CachedAvatarFixture();

        await cache.WriteAvatarAsync(Owner.UserId, avatar.Value);

        var read = await cache.ReadAvatarAsync(Owner.UserId);

        Assert.NotNull(read);
        Assert.Equal(avatar.Value.Content, read.Content);
        Assert.Equal("image/jpeg", read.ContentType);
        Assert.Equal("\"639212800319451970\"", read.ETag);
        Assert.Equal(avatar.Value.Marker, read.Marker);
    }

    [Fact]
    public async Task Another_account_is_not_handed_this_one_s_picture()
    {
        var cache = LocalStore.InMemory();

        await cache.WriteAvatarAsync(Owner.UserId, new CachedAvatarFixture().Value);

        // Sign-out clears the row, so this should be unreachable. It is checked anyway,
        // because "unreachable" is a poor reason to show someone else's photograph.
        Assert.Null(await cache.ReadAvatarAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_missing_tag_stays_missing()
    {
        var cache = LocalStore.InMemory();

        // What an upload this device made leaves behind: the PUT response carries the marker
        // but no ETag, and inventing one would make the next conditional request lie.
        await cache.WriteAvatarAsync(
            Owner.UserId,
            new CachedAvatarFixture { ETag = null }.Value);

        Assert.Null((await cache.ReadAvatarAsync(Owner.UserId))?.ETag);
    }

    [Fact]
    public async Task Signing_out_leaves_nothing_behind()
    {
        var cache = LocalStore.InMemory();

        await cache.WriteAccountAsync(Owner);
        await cache.WriteAvatarAsync(Owner.UserId, new CachedAvatarFixture().Value);

        await cache.ClearAsync();

        Assert.Null(await cache.ReadAccountAsync());
        Assert.Null(await cache.ReadAvatarAsync(Owner.UserId));
    }

    [Fact]
    public async Task Clearing_the_picture_keeps_the_account()
    {
        var cache = LocalStore.InMemory();

        await cache.WriteAccountAsync(Owner);
        await cache.WriteAvatarAsync(Owner.UserId, new CachedAvatarFixture().Value);

        // Removing a picture is not signing out: the profile still has to render offline.
        await cache.ClearAvatarAsync();

        Assert.Null(await cache.ReadAvatarAsync(Owner.UserId));
        Assert.NotNull(await cache.ReadAccountAsync());
    }

    private sealed class CachedAvatarFixture
    {
        public string? ETag { get; init; } = "\"639212800319451970\"";

        public Trackr.Mobile.Core.Storage.CachedAvatar Value => new(
            [0xFF, 0xD8, 0xFF, 0xE0],
            "image/jpeg",
            ETag,
            new DateTimeOffset(2026, 8, 2, 15, 9, 42, 426, TimeSpan.Zero));
    }
}
