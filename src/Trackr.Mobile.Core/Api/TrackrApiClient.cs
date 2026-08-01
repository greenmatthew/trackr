using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Trackr.Mobile.Core.Platform;
using Trackr.Shared.Auth;
using Trackr.Shared.Health;

namespace Trackr.Mobile.Core.Api;

/// <summary>
/// Talks to the Trackr API over HTTP.
/// </summary>
/// <remarks>
/// Builds absolute URIs from <see cref="IServerSettings"/> on every call rather than setting
/// <c>HttpClient.BaseAddress</c> once. The server address is not known at startup - it is
/// typed by the user during first-run setup and can be changed afterwards - and a
/// <c>BaseAddress</c> cannot be reassigned once the client has sent a request.
/// </remarks>
public sealed class TrackrApiClient(
    HttpClient http,
    IServerSettings serverSettings,
    ILogger<TrackrApiClient> logger) : ITrackrApiClient
{
    public async Task<ServerCheckResult> CheckServerAsync(
        Uri baseUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // The liveness endpoint: anonymous, and 200 whenever the process is up even if
            // Postgres is down. Checking readiness instead would reject a server that is
            // running perfectly well through a brief database blip.
            using var response = await http.GetAsync(
                new Uri(baseUrl, "api/health/live"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return ServerCheckResult.Failed(
                    $"The server answered with {(int)response.StatusCode}. Check the address is right.");
            }

            return ServerCheckResult.Reachable;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Server check failed for {BaseUrl}", baseUrl);

            // Overwhelmingly the most common self-hosted failure: a certificate Android does
            // not trust, because the reverse proxy uses a self-signed or private-CA cert.
            // Worth naming explicitly - the generic message sends people hunting the wrong
            // problem entirely.
            var isTls = ex.InnerException is System.Security.Authentication.AuthenticationException;

            return ServerCheckResult.Failed(isTls
                ? "Could not verify the server's HTTPS certificate. If it is self-signed, install its "
                  + "certificate on this phone first."
                : "Could not reach that address. Check the server is running and that this phone is on "
                  + "the right network or VPN.");
        }
        catch (TaskCanceledException)
        {
            return ServerCheckResult.Failed("The server took too long to answer.");
        }
    }

    public async Task<SignInResult> SignInAsync(
        TokenRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                Endpoint("api/auth/token"),
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);

                return tokens is null
                    ? new SignInResult(LoginStatus.Failed, Problem: "The server returned an empty response.")
                    : new SignInResult(LoginStatus.Succeeded, tokens);
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized)
            {
                // The endpoint answers 401 with a status body rather than a problem
                // document, because "wrong password" and "you owe a 2FA code" are normal
                // outcomes to branch on, not errors to render verbatim.
                var body = await response.Content.ReadFromJsonAsync<TokenLoginResponse>(cancellationToken);

                if (body is not null)
                {
                    return new SignInResult(body.Status, LockoutEndUtc: body.LockoutEndUtc);
                }
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                // CLAUDE.md section 5: rate limits reach the user as a plain message rather
                // than vanishing into a generic failure.
                return new SignInResult(
                    LoginStatus.Failed,
                    Problem: "Too many attempts. Wait a minute and try again.");
            }

            return new SignInResult(
                LoginStatus.Failed,
                Problem: $"The server answered with {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Sign-in request failed");

            return new SignInResult(
                LoginStatus.Failed,
                Problem: "Could not reach the server. Check your connection.");
        }
    }

    public async Task<TokenResponse?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                Endpoint("api/auth/token/refresh"),
                new RefreshRequest { RefreshToken = refreshToken },
                cancellationToken);

            // A 401 here is expected and not an error: the refresh token expired, or the
            // password changed and rolled the security stamp. Either way the answer is to
            // sign in again, which the caller handles.
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Token refresh failed");

            return null;
        }
    }

    public async Task<MeResponse?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // The bearer token is attached by BearerTokenHandler, not here.
            using var response = await http.GetAsync(Endpoint("api/auth/me"), cancellationToken);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken)
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Identity lookup failed");

            return null;
        }
    }

    private Uri Endpoint(string relativePath) =>
        serverSettings.BaseUrl is { } baseUrl
            ? new Uri(baseUrl, relativePath)
            : throw new InvalidOperationException(
                "No server address is configured. First-run setup must complete before any API call.");
}
