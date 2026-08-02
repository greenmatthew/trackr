using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.Storage;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Tests;

/// <summary>
/// The rules about when the profile picture is fetched, kept, and dropped.
/// </summary>
/// <remarks>
/// Worth testing without a device because none of it is drawing: it is the marker arithmetic
/// that decides whether the phone spends a request, and getting it wrong is invisible until
/// someone notices the app re-downloading an image on every screen.
/// </remarks>
public sealed class AvatarStoreTests
{
    private static readonly DateTimeOffset Marker = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly byte[] Bytes = [1, 2, 3];

    [Fact]
    public async Task An_account_with_no_picture_is_never_asked_for_one()
    {
        var (store, api, _, _) = Build(avatarUpdatedUtc: null);

        await store.EnsureLoadedAsync();

        Assert.False(store.HasPicture);
        await api.DidNotReceive().GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_picture_is_fetched_once_and_then_left_alone()
    {
        var (store, api, _, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();
        await store.EnsureLoadedAsync();

        Assert.Equal(Bytes, store.Content);

        // The second call is the point: the marker on the session has not moved, so there is
        // nothing to ask about. Re-fetching here would mean an image download per screen.
        await api.Received(1).GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_newer_marker_re_asks_but_sends_the_tag_it_already_holds()
    {
        var (store, api, session, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        session.NoteAvatarChanged(Marker.AddMinutes(1));

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AvatarFetchResult.Unchanged);

        await store.EnsureLoadedAsync();

        // Conditional, so the server can answer 304 with headers instead of an image.
        await api.Received(1).GetAvatarAsync("\"1\"", Arg.Any<CancellationToken>());
        Assert.Equal(Bytes, store.Content);
    }

    [Fact]
    public async Task A_304_settles_the_question_rather_than_asking_again()
    {
        var (store, api, session, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        session.NoteAvatarChanged(Marker.AddMinutes(1));

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AvatarFetchResult.Unchanged);

        await store.EnsureLoadedAsync();
        await store.EnsureLoadedAsync();

        // Three EnsureLoadedAsync calls, two requests: the 304 has to move the held marker
        // forward, or every later call would re-ask about a picture already known current.
        await api.Received(2).GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_picture_removed_elsewhere_is_dropped_here()
    {
        var (store, api, session, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        session.NoteAvatarChanged(Marker.AddMinutes(1));

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AvatarFetchResult.None);

        await store.EnsureLoadedAsync();

        Assert.False(store.HasPicture);
    }

    [Fact]
    public async Task A_failed_fetch_keeps_what_is_already_shown()
    {
        var (store, api, session, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        session.NoteAvatarChanged(Marker.AddMinutes(1));

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AvatarFetchResult.Failed);

        await store.EnsureLoadedAsync();

        // Losing the picture because the wifi dropped would be worse than showing one that
        // may be a version behind.
        Assert.Equal(Bytes, store.Content);
    }

    [Fact]
    public async Task Uploading_shows_the_new_picture_without_fetching_it_back()
    {
        var (store, api, session, _) = Build(avatarUpdatedUtc: null);

        var uploaded = new byte[] { 9, 9, 9 };

        api.UploadAvatarAsync(uploaded, "image/jpeg", Arg.Any<CancellationToken>())
            .Returns(AvatarChangeResult.Ok(Marker));

        var raised = 0;
        store.Changed += () => raised++;

        var result = await store.ReplaceAsync(uploaded, "image/jpeg");

        Assert.True(result.Succeeded);
        Assert.Equal(uploaded, store.Content);
        Assert.Equal(1, raised);

        // The session's marker has to move with it. Without that, the next EnsureLoadedAsync
        // would compare against a stale null and throw the new picture away.
        Assert.Equal(Marker, session.CurrentUser?.AvatarUpdatedUtc);

        await store.EnsureLoadedAsync();

        await api.DidNotReceive().GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_rejected_upload_leaves_the_old_picture_in_place()
    {
        var (store, api, _, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        api.UploadAvatarAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(AvatarChangeResult.Failed("Too large."));

        var result = await store.ReplaceAsync([4, 5, 6], "image/jpeg");

        Assert.False(result.Succeeded);
        Assert.Equal(Bytes, store.Content);
    }

    [Fact]
    public async Task Removing_clears_the_picture_and_the_marker()
    {
        var (store, api, session, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        api.DeleteAvatarAsync(Arg.Any<CancellationToken>()).Returns(AvatarChangeResult.Ok());

        await store.RemoveAsync();

        Assert.False(store.HasPicture);
        Assert.Null(session.CurrentUser?.AvatarUpdatedUtc);
    }

    [Fact]
    public async Task Signing_out_drops_the_bytes()
    {
        var (store, api, session, _) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        await session.SignOutAsync();

        // Not merely stopped being drawn: the previous account's photograph must not be
        // sitting in memory for the next one (CLAUDE.md section 8).
        Assert.False(store.HasPicture);
        Assert.Null(store.ETag);
    }

    [Fact]
    public async Task A_picture_kept_from_the_last_launch_is_shown_before_the_server_answers()
    {
        var (store, api, session, cache) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        // A second run of the app: everything in memory is gone, the database is not. The
        // account's marker has moved on since, as it would if another device had touched the
        // picture - so this launch has a reason to ask.
        var relaunched = new AvatarStore(api, session, cache);

        session.NoteAvatarChanged(Marker.AddMinutes(1));

        api.ClearReceivedCalls();
        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(AvatarFetchResult.Unchanged);

        await relaunched.EnsureLoadedAsync();

        Assert.Equal(Bytes, relaunched.Content);

        // And the tag came back with it, so the check costs a 304 rather than an image. That
        // is the entire point of storing the bytes: without them the ETag has nothing to
        // validate and every launch re-downloads.
        await api.Received(1).GetAvatarAsync("\"1\"", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_stored_picture_already_at_the_current_marker_costs_no_request_at_all()
    {
        var (store, api, session, cache) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        var relaunched = new AvatarStore(api, session, cache);

        api.ClearReceivedCalls();

        await relaunched.EnsureLoadedAsync();

        // The marker the bytes were stored against still matches the account's, so there is
        // nothing to ask - not even conditionally.
        Assert.Equal(Bytes, relaunched.Content);
        await api.DidNotReceive().GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Removing_the_picture_takes_the_stored_copy_with_it()
    {
        var (store, api, session, cache) = Build(Marker);

        api.GetAvatarAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new AvatarFetchResult(AvatarFetchStatus.Fetched, Bytes, "image/jpeg", "\"1\""));

        await store.EnsureLoadedAsync();

        api.DeleteAvatarAsync(Arg.Any<CancellationToken>()).Returns(AvatarChangeResult.Ok());

        await store.RemoveAsync();

        // Otherwise the next launch reads it straight back off disk and the picture returns
        // from the dead.
        Assert.Null(await cache.ReadAvatarAsync(session.CurrentUser!.UserId));
    }

    /// <summary>
    /// A session already signed in as an account whose picture marker is
    /// <paramref name="avatarUpdatedUtc"/>.
    /// </summary>
    private static (
        AvatarStore Store,
        ITrackrApiClient Api,
        AuthSession Session,
        AccountCache Cache) Build(DateTimeOffset? avatarUpdatedUtc)
    {
        var api = Substitute.For<ITrackrApiClient>();
        var tokenStore = Substitute.For<ITokenStore>();
        var serverSettings = Substitute.For<IServerSettings>();

        serverSettings.BaseUrl.Returns(new Uri("https://trackr.example.test/"));
        tokenStore.ReadAsync().Returns(new StoredTokens("access", "refresh", DateTimeOffset.MaxValue));
        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Ok(new MeResponse(
            Guid.NewGuid(),
            "owner@example.test",
            TwoFactorEnabled: false,
            avatarUpdatedUtc)));

        // One cache for both, as in the app: the session writes the account into it and the
        // store keeps the picture beside it.
        var cache = LocalStore.InMemory();

        var session = new AuthSession(api, tokenStore, serverSettings, cache);
        var store = new AvatarStore(api, session, cache);

        // Signs in via the same path the app uses, so CurrentUser is populated the same way.
        session.RestoreAsync().GetAwaiter().GetResult();

        return (store, api, session, cache);
    }
}
