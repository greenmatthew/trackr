using Trackr.Shared.Auth;

namespace Trackr.Mobile.Core.Api;

/// <summary>
/// Every call the app makes to the server, in one place.
/// </summary>
/// <remarks>
/// An interface so the view-model tests can substitute it - there is no equivalent of the
/// API project's "drive the real application" approach on this side of the wire.
/// </remarks>
public interface ITrackrApiClient
{
    /// <summary>
    /// Checks that a candidate address is actually a reachable Trackr server, during
    /// first-run setup. Deliberately takes the URL rather than reading
    /// <see cref="Platform.IServerSettings"/>, because at this point nothing is saved yet.
    /// </summary>
    Task<ServerCheckResult> CheckServerAsync(Uri baseUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the server will accept a new account freely or only with an invite. Null when
    /// the question could not be asked at all.
    /// </summary>
    Task<RegistrationMode?> GetRegistrationModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an account: the first one on an empty server, or one redeeming an invite.
    /// </summary>
    /// <remarks>
    /// Does <b>not</b> sign the caller in. The endpoint issues a cookie, which is no use to a
    /// native client, so the app follows a success with <see cref="SignInAsync"/>.
    /// </remarks>
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>Password (and optionally 2FA) sign-in, returning bearer tokens.</summary>
    Task<SignInResult> SignInAsync(TokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for a new pair. Null when the refresh token is spent.</summary>
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Who the stored token belongs to. Also the app's "am I still signed in" probe.</summary>
    Task<MeResult> GetMeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The account's profile picture.
    /// </summary>
    /// <param name="knownETag">
    /// The tag of the copy the caller already holds, if any. The server answers 304 when it
    /// still matches, which is the whole point of keeping the tag: the app re-checks on every
    /// launch and almost always gets headers back instead of an image.
    /// </param>
    Task<AvatarFetchResult> GetAvatarAsync(
        string? knownETag = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the profile picture. The bytes must already be within
    /// <see cref="Trackr.Shared.Auth.AvatarRules"/> - the server enforces them regardless.
    /// </summary>
    Task<AvatarChangeResult> UploadAvatarAsync(
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the profile picture, falling back to initials. Idempotent.</summary>
    Task<AvatarChangeResult> DeleteAvatarAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of probing a candidate server address.</summary>
/// <param name="IsReachable">Whether a Trackr server answered.</param>
/// <param name="Problem">A message fit to show the user. Null when reachable.</param>
public sealed record ServerCheckResult(bool IsReachable, string? Problem = null)
{
    public static ServerCheckResult Reachable { get; } = new(true);

    public static ServerCheckResult Failed(string problem) => new(false, problem);
}

/// <summary>Outcome of creating an account.</summary>
/// <param name="Succeeded">Whether the account now exists.</param>
/// <param name="Problem">
/// A message fit to show the user, null on success. Carries the server's own wording where it
/// has any - Identity's "passwords must be at least 12 characters" is far more useful than
/// anything this app could infer from a 400.
/// </param>
public sealed record RegisterResult(bool Succeeded, string? Problem = null)
{
    public static RegisterResult Ok { get; } = new(true);

    public static RegisterResult Failed(string problem) => new(false, problem);
}

/// <summary>
/// Outcome of a sign-in attempt.
/// </summary>
/// <param name="Status">
/// What happened. <see cref="LoginStatus.RequiresTwoFactor"/> means the password was right
/// and the caller should prompt for a code and try again - it is not a failure.
/// </param>
/// <param name="Tokens">The issued tokens, set only when <paramref name="Status"/> is Succeeded.</param>
/// <param name="LockoutEndUtc">When the account unlocks, for LockedOut.</param>
/// <param name="Problem">Set when the request never reached a verdict, e.g. the server was unreachable.</param>
public sealed record SignInResult(
    LoginStatus Status,
    TokenResponse? Tokens = null,
    DateTimeOffset? LockoutEndUtc = null,
    string? Problem = null);

/// <summary>Why an identity lookup did not produce an account.</summary>
/// <remarks>
/// The distinction is the whole point: "the server says this session is over" ends it, while
/// "the server could not be reached" must not - otherwise a phone in a tunnel signs itself
/// out. The two used to arrive as the same null.
/// </remarks>
public enum MeStatus
{
    Succeeded,

    /// <summary>The server answered, and the answer was that this token is no good.</summary>
    SignedOut,

    /// <summary>No usable answer: offline, timed out, or the server itself is unwell.</summary>
    Unreachable,
}

/// <param name="User">The account, set only when <paramref name="Status"/> is Succeeded.</param>
public sealed record MeResult(MeStatus Status, MeResponse? User = null)
{
    public static MeResult SignedOut { get; } = new(MeStatus.SignedOut);

    public static MeResult Unreachable { get; } = new(MeStatus.Unreachable);

    public static MeResult Ok(MeResponse user) => new(MeStatus.Succeeded, user);
}

/// <summary>What asking for the profile picture produced.</summary>
public enum AvatarFetchStatus
{
    /// <summary>The picture came back and is in <see cref="AvatarFetchResult.Content"/>.</summary>
    Fetched,

    /// <summary>The copy the caller already holds is still current. Nothing was transferred.</summary>
    Unchanged,

    /// <summary>The account has no picture. Not an error - it is the default state.</summary>
    None,

    /// <summary>The question could not be asked. The caller keeps whatever it had.</summary>
    Failed,
}

/// <param name="ETag">
/// The server's tag for these bytes, to send back on the next request. Null unless
/// <paramref name="Status"/> is <see cref="AvatarFetchStatus.Fetched"/>.
/// </param>
public sealed record AvatarFetchResult(
    AvatarFetchStatus Status,
    byte[]? Content = null,
    string? ContentType = null,
    string? ETag = null)
{
    public static AvatarFetchResult Unchanged { get; } = new(AvatarFetchStatus.Unchanged);

    public static AvatarFetchResult None { get; } = new(AvatarFetchStatus.None);

    public static AvatarFetchResult Failed { get; } = new(AvatarFetchStatus.Failed);
}

/// <summary>Outcome of setting or removing the profile picture.</summary>
/// <param name="UpdatedUtc">
/// The account's new avatar marker - the value <c>GET /api/auth/me</c> will report from now
/// on. Null after a removal, because there is no longer a picture to have a marker.
/// </param>
public sealed record AvatarChangeResult(
    bool Succeeded,
    DateTimeOffset? UpdatedUtc = null,
    string? Problem = null)
{
    public static AvatarChangeResult Ok(DateTimeOffset? updatedUtc = null) => new(true, updatedUtc);

    public static AvatarChangeResult Failed(string problem) => new(false, Problem: problem);
}
