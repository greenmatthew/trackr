using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Platform;
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
    IServerSettings serverSettings)
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
    public async Task<bool> RestoreAsync()
    {
        if (!HasServer || await tokenStore.ReadAsync() is null)
        {
            return false;
        }

        CurrentUser = await api.GetMeAsync();
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

            CurrentUser = await api.GetMeAsync(cancellationToken);
            Changed?.Invoke();
        }

        return result;
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

        CurrentUser = null;
        Changed?.Invoke();
    }
}
