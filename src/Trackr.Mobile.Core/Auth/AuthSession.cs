using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.Storage;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Core.Auth;

/// <summary>
/// The app's answer to "who is signed in, and to which server".
/// </summary>
/// <remarks>
/// The counterpart to the web app's <c>CookieAuthenticationStateProvider</c>. It plays the
/// same role - single source of auth state, asked at startup, invalidated on sign-in and
/// sign-out - but it cannot be the same class, because that one deliberately holds no token
/// and asks the server on every check. Here the token is the state.
/// </remarks>
public sealed class AuthSession(
    ITrackrApiClient api,
    ITokenStore tokenStore,
    IServerSettings serverSettings,
    AccountCache cache)
{
    /// <summary>The signed-in account, or null. Populated by <see cref="RestoreAsync"/>.</summary>
    public MeResponse? CurrentUser { get; private set; }

    public bool IsSignedIn => CurrentUser is not null;

    /// <summary>Whether first-run setup has happened at all.</summary>
    public bool HasServer => serverSettings.BaseUrl is not null;

    /// <summary>Raised when sign-in state changes, so the shell can swap what it shows.</summary>
    public event Action? Changed;

    /// <summary>
    /// Works out at startup whether there is a usable session, by spending the stored token
    /// on a real request. Asking the server is the only honest answer: the token may have
    /// been revoked by a password change on another device, and a locally valid expiry date
    /// would say nothing about that.
    /// </summary>
    /// <remarks>
    /// When the server cannot be reached at all, the last account it told us about is used
    /// instead, so a phone with no signal opens on the app rather than on the login screen.
    /// That is a weaker answer than asking, and it is scoped to exactly the case where asking
    /// is impossible: a server that answers and says no still signs the user out.
    /// <para>
    /// Safe because of one invariant - the cached account is only ever written by a
    /// successful <c>/me</c>, and sign-out clears it - so the cache always describes the
    /// owner of the stored token rather than some previous user of this device. Nothing else
    /// may write it, which is why <see cref="SignInAsync"/> deliberately does not fall back
    /// the same way.
    /// </para>
    /// </remarks>
    public async Task<bool> RestoreAsync()
    {
        if (!HasServer || await tokenStore.ReadAsync() is null)
        {
            return false;
        }

        var result = await api.GetMeAsync();

        CurrentUser = result switch
        {
            { Status: MeStatus.Succeeded, User: { } user } => await RememberAsync(user),
            { Status: MeStatus.Unreachable } => await cache.ReadAccountAsync(),
            _ => null,
        };

        Changed?.Invoke();

        return IsSignedIn;
    }

    /// <summary>
    /// Signs in and, on success, persists the tokens.
    /// </summary>
    /// <remarks>
    /// <see cref="LoginStatus.RequiresTwoFactor"/> is not a failure - it means the password
    /// was accepted and the caller should collect a code and call this again with the whole
    /// request. See <see cref="TokenRequest"/> for why there is no server-side challenge to
    /// hold on to in between.
    /// </remarks>
    public async Task<SignInResult> SignInAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        var result = await api.SignInAsync(request, cancellationToken);

        if (result is { Status: LoginStatus.Succeeded, Tokens: { } tokens })
        {
            await tokenStore.WriteAsync(new StoredTokens(
                tokens.AccessToken,
                tokens.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn)));

            // No offline fallback here, unlike RestoreAsync: these tokens are brand new and
            // may belong to a different account than the one cached, so falling back would
            // risk showing the previous user's details to this one.
            var me = await api.GetMeAsync(cancellationToken);

            CurrentUser = me is { Status: MeStatus.Succeeded, User: { } user }
                ? await RememberAsync(user, cancellationToken)
                : null;

            Changed?.Invoke();
        }

        return result;
    }

    /// <summary>
    /// Keeps the account for the next launch, and hands it back so the caller can assign it.
    /// </summary>
    private async Task<MeResponse> RememberAsync(
        MeResponse user,
        CancellationToken cancellationToken = default)
    {
        await cache.WriteAccountAsync(user, cancellationToken);

        return user;
    }

    /// <summary>
    /// Records that this device just changed the profile picture, so the avatar marker here
    /// matches what the server would now report.
    /// </summary>
    /// <remarks>
    /// The alternative is re-fetching <c>/me</c> after every upload, which is a round trip to
    /// learn a value the upload already returned. Deliberately does not raise
    /// <see cref="Changed"/>: sign-in state has not changed, and the shell must not be swapped
    /// because someone chose a photograph.
    /// </remarks>
    public void NoteAvatarChanged(DateTimeOffset? updatedUtc)
    {
        if (CurrentUser is { } user)
        {
            CurrentUser = user with { AvatarUpdatedUtc = updatedUtc };
        }
    }

    public async Task SignOutAsync()
    {
        // Local only. There is no server call to make: bearer tokens are stateless, so the
        // access token stays technically valid until it expires. Clearing the refresh token
        // is what actually ends the session, and rotating the data-protection keys is the
        // blunt instrument if a device is lost - see docs/decisions/03-android-pivot.md.
        await tokenStore.ClearAsync();

        // The cached account and picture go with it. Leaving them would hand the next person
        // to sign in on this device the previous one's email and photograph, and would break
        // the invariant RestoreAsync leans on.
        await cache.ClearAsync();

        CurrentUser = null;
        Changed?.Invoke();
    }
}
