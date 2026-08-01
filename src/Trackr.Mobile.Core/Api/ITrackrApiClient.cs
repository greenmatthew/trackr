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

    /// <summary>Password (and optionally 2FA) sign-in, returning bearer tokens.</summary>
    Task<SignInResult> SignInAsync(TokenRequest request, CancellationToken cancellationToken = default);

    /// <summary>Exchanges a refresh token for a new pair. Null when the refresh token is spent.</summary>
    Task<TokenResponse?> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Who the stored token belongs to. Also the app's "am I still signed in" probe.</summary>
    Task<MeResponse?> GetMeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of probing a candidate server address.</summary>
/// <param name="IsReachable">Whether a Trackr server answered.</param>
/// <param name="Problem">A message fit to show the user. Null when reachable.</param>
public sealed record ServerCheckResult(bool IsReachable, string? Problem = null)
{
    public static ServerCheckResult Reachable { get; } = new(true);

    public static ServerCheckResult Failed(string problem) => new(false, problem);
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
