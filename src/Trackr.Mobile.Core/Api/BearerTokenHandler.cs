using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Trackr.Mobile.Core.Platform;
using Trackr.Shared.Auth;

namespace Trackr.Mobile.Core.Api;

/// <summary>
/// Attaches the stored access token to every request, and renews it when it has expired.
/// </summary>
/// <remarks>
/// The counterpart to the web app's <c>UnauthorizedResponseHandler</c>, and the reason
/// <c>Microsoft.Extensions.Http</c> is referenced at all: <c>AddHttpMessageHandler</c> is
/// how this gets into the pipeline.
/// <para>
/// Refreshes are serialised through a semaphore. Several screens can easily fire requests at
/// once on resume, and without the lock each would independently notice the 401 and post its
/// own refresh - spending the single-use refresh token several times over, with all but one
/// attempt failing and signing the user out for no reason.
/// </para>
/// </remarks>
public sealed class BearerTokenHandler(
    ITokenStore tokenStore,
    IServerSettings serverSettings,
    ILogger<BearerTokenHandler> logger) : DelegatingHandler
{
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var tokens = await tokenStore.ReadAsync();

        // Signed out, or a call that runs before sign-in such as the server check. Pass it
        // through unauthenticated and let the endpoint decide.
        if (tokens is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // Refresh slightly early. A token that expires while in flight would otherwise cost
        // a wasted round trip and a retry.
        if (tokens.AccessTokenExpiresUtc <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            tokens = await RefreshAsync(tokens, cancellationToken);

            if (tokens is null)
            {
                return await base.SendAsync(request, cancellationToken);
            }
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        var response = await base.SendAsync(request, cancellationToken);

        // A 401 despite a token we believed was valid: the server may have rolled the
        // security stamp (password or 2FA change elsewhere). Worth exactly one refresh
        // attempt before giving up.
        if (response.StatusCode is not HttpStatusCode.Unauthorized)
        {
            return response;
        }

        var renewed = await RefreshAsync(tokens, cancellationToken);
        if (renewed is null)
        {
            return response;
        }

        response.Dispose();

        // A request cannot be sent twice, so the retry needs a copy.
        var retry = await CloneAsync(request, cancellationToken);
        retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", renewed.AccessToken);

        return await base.SendAsync(retry, cancellationToken);
    }

    /// <summary>
    /// Exchanges the refresh token for a new pair and stores it. Null means the session is
    /// over and the user has to sign in again.
    /// </summary>
    private async Task<StoredTokens?> RefreshAsync(
        StoredTokens current,
        CancellationToken cancellationToken)
    {
        await RefreshLock.WaitAsync(cancellationToken);

        try
        {
            // Another caller may have refreshed while this one queued, in which case there
            // is nothing to do.
            var latest = await tokenStore.ReadAsync();
            if (latest is not null && latest.AccessToken != current.AccessToken)
            {
                return latest;
            }

            if (serverSettings.BaseUrl is not { } baseUrl)
            {
                return null;
            }

            // Deliberately a bare HttpClient rather than the one this handler is installed
            // in: routing the refresh back through itself would recurse.
            using var client = new HttpClient();
            using var response = await client.PostAsJsonAsync(
                new Uri(baseUrl, "api/auth/token/refresh"),
                new RefreshRequest { RefreshToken = current.RefreshToken },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("Refresh token rejected; the session is over.");
                await tokenStore.ClearAsync();

                return null;
            }

            var issued = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (issued is null)
            {
                return null;
            }

            var stored = new StoredTokens(
                issued.AccessToken,
                issued.RefreshToken,
                DateTimeOffset.UtcNow.AddSeconds(issued.ExpiresIn));

            await tokenStore.WriteAsync(stored);

            return stored;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Offline. Deliberately does NOT clear the stored tokens - they may be perfectly
            // good, and signing someone out because their train went into a tunnel would be
            // its own bug.
            logger.LogWarning(ex, "Could not reach the server to refresh the token.");

            return null;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    private static async Task<HttpRequestMessage> CloneAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content is not null)
        {
            // Buffer the body: the original content stream has already been consumed.
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var option in request.Options)
        {
            clone.Options.TryAdd(option.Key, option.Value);
        }

        return clone;
    }
}
