using NSubstitute;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.Storage;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Restoring a session at launch, and the one case where the cache stands in for the server.
/// </summary>
/// <remarks>
/// The distinction under test is the reason <c>GetMeAsync</c> reports a status rather than
/// returning a nullable account: "the server says no" and "there is no server to ask" used to
/// arrive as the same null, and only one of them should end a session.
/// </remarks>
public sealed class AuthSessionTests
{
    private static readonly MeResponse Owner = new(
        Guid.NewGuid(),
        "owner@example.test",
        TwoFactorEnabled: false);

    [Fact]
    public async Task A_successful_lookup_signs_in_and_is_remembered()
    {
        var (session, api, _, cache) = Build();

        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Ok(Owner));

        Assert.True(await session.RestoreAsync());
        Assert.Equal(Owner, session.CurrentUser);

        // Written on the way past, because it is what the *next* launch falls back on.
        Assert.Equal(Owner, await cache.ReadAccountAsync());
    }

    [Fact]
    public async Task An_unreachable_server_opens_on_the_last_known_account()
    {
        var (session, api, _, cache) = Build();

        await cache.WriteAccountAsync(Owner);

        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Unreachable);

        // Signing someone out because their phone has no signal would be its own bug. The
        // token is untouched and the next launch with a connection asks properly.
        Assert.True(await session.RestoreAsync());
        Assert.Equal(Owner, session.CurrentUser);
    }

    [Fact]
    public async Task An_unreachable_server_with_nothing_cached_stays_signed_out()
    {
        var (session, api, _, _) = Build();

        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Unreachable);

        Assert.False(await session.RestoreAsync());
        Assert.Null(session.CurrentUser);
    }

    [Fact]
    public async Task A_rejected_token_signs_out_even_with_a_cached_account()
    {
        var (session, api, _, cache) = Build();

        await cache.WriteAccountAsync(Owner);

        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.SignedOut);

        // The server answered, and the answer was no. The cache exists for the case where
        // there is no answer at all - using it here would keep a revoked session alive.
        Assert.False(await session.RestoreAsync());
        Assert.Null(session.CurrentUser);
    }

    [Fact]
    public async Task Signing_in_when_the_lookup_fails_does_not_borrow_the_cached_account()
    {
        var (session, api, _, cache) = Build();

        // A different account is already cached on this device.
        await cache.WriteAccountAsync(Owner);

        api.SignInAsync(Arg.Any<TokenRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SignInResult(
                LoginStatus.Succeeded,
                new TokenResponse("Bearer", "access", 3600, "refresh")));
        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Unreachable);

        await session.SignInAsync(new TokenRequest { Email = "other@example.test", Password = "x" });

        // These tokens may belong to somebody else entirely, so there is no honest fallback.
        Assert.Null(session.CurrentUser);
    }

    [Fact]
    public async Task Signing_out_empties_the_cache()
    {
        var (session, api, _, cache) = Build();

        api.GetMeAsync(Arg.Any<CancellationToken>()).Returns(MeResult.Ok(Owner));

        await session.RestoreAsync();
        await session.SignOutAsync();

        // Otherwise the next person to sign in on this phone gets the previous one's email
        // rendered at them, and RestoreAsync's invariant no longer holds.
        Assert.Null(await cache.ReadAccountAsync());
    }

    private static (
        AuthSession Session,
        ITrackrApiClient Api,
        ITokenStore TokenStore,
        AccountCache Cache) Build()
    {
        var api = Substitute.For<ITrackrApiClient>();
        var tokenStore = Substitute.For<ITokenStore>();
        var serverSettings = Substitute.For<IServerSettings>();
        var cache = LocalStore.InMemory();

        serverSettings.BaseUrl.Returns(new Uri("https://trackr.example.test/"));
        tokenStore.ReadAsync().Returns(new StoredTokens("access", "refresh", DateTimeOffset.MaxValue));

        return (new AuthSession(api, tokenStore, serverSettings, cache), api, tokenStore, cache);
    }
}
