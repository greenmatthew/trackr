using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
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
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            logger.LogWarning(ex, "Server check failed for {BaseUrl}", baseUrl);

            return ServerCheckResult.Failed(Describe(ex));
        }
    }

    public async Task<RegistrationMode?> GetRegistrationModeAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(
                Endpoint("api/auth/registration-status"),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var status = await response.Content
                .ReadFromJsonAsync<RegistrationStatusResponse>(cancellationToken);

            return status?.Mode;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            logger.LogWarning(ex, "Registration status lookup failed");

            // Null rather than a guess. The caller assumes the stricter of the two modes, so
            // a failure here never opens up a form that should have asked for an invite.
            return null;
        }
    }

    public async Task<RegisterResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                Endpoint("api/auth/register"),
                request,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return RegisterResult.Ok;
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                // CLAUDE.md section 5: rate limits reach the user as a plain message rather
                // than vanishing into a generic failure. This one is easy to hit honestly -
                // the register endpoint allows only five attempts every fifteen minutes.
                return RegisterResult.Failed(
                    "Too many attempts. Wait a few minutes and try again.");
            }

            return RegisterResult.Failed(await ReadProblemAsync(response, cancellationToken));
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            logger.LogWarning(ex, "Registration request failed");

            return RegisterResult.Failed("Could not reach the server. Check your connection.");
        }
    }

    /// <summary>
    /// Turns an RFC 9457 problem document into one line of prose.
    /// </summary>
    /// <remarks>
    /// Only the fields this app actually renders. The server puts the messages that matter in
    /// two different places: Identity's password and duplicate-email complaints arrive as
    /// per-field <c>errors</c>, while "registration is closed" arrives as <c>detail</c>. Both
    /// are worth showing verbatim, so neither is flattened into a generic failure.
    /// </remarks>
    private static async Task<string> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<ProblemPayload>(cancellationToken);

            if (problem?.Errors is { Count: > 0 } errors)
            {
                return string.Join(" ", errors.SelectMany(entry => entry.Value));
            }

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem.Detail;
            }

            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                return problem.Title;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // An unparseable body still has to produce a message, not a silent failure.
        }

        return $"The server answered with {(int)response.StatusCode}.";
    }

    /// <summary>The subset of problem+json this app reads.</summary>
    private sealed class ProblemPayload
    {
        public string? Title { get; set; }

        public string? Detail { get; set; }

        public Dictionary<string, string[]>? Errors { get; set; }
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
        catch (Exception ex) when (IsTransportFailure(ex))
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
        catch (Exception ex) when (IsTransportFailure(ex))
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
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            logger.LogWarning(ex, "Identity lookup failed");

            return null;
        }
    }

    public async Task<AvatarFetchResult> GetAvatarAsync(
        string? knownETag = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint("api/account/avatar"));

            if (!string.IsNullOrWhiteSpace(knownETag))
            {
                // TryAddWithoutValidation because the value is the server's own tag, quotes
                // and all, handed straight back. Parsing it apart only to reassemble it
                // would be a chance to get it subtly wrong.
                request.Headers.TryAddWithoutValidation("If-None-Match", knownETag);
            }

            using var response = await http.SendAsync(request, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotModified)
            {
                return AvatarFetchResult.Unchanged;
            }

            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                // Having no picture is the ordinary state, not a failure. The caller draws
                // initials instead.
                return AvatarFetchResult.None;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Avatar fetch answered with {StatusCode}",
                    (int)response.StatusCode);

                return AvatarFetchResult.Failed;
            }

            return new AvatarFetchResult(
                AvatarFetchStatus.Fetched,
                await response.Content.ReadAsByteArrayAsync(cancellationToken),
                response.Content.Headers.ContentType?.MediaType,
                response.Headers.ETag?.ToString());
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            logger.LogWarning(ex, "Avatar fetch failed");

            return AvatarFetchResult.Failed;
        }
    }

    public async Task<AvatarChangeResult> UploadAvatarAsync(
        byte[] content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Raw bytes with a Content-Type, not multipart: there is exactly one file and no
            // fields to go with it, so a form part would be envelope around nothing.
            using var body = new ByteArrayContent(content);
            body.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            using var response = await http.PutAsync(
                Endpoint("api/account/avatar"),
                body,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content
                    .ReadFromJsonAsync<AvatarResponse>(cancellationToken);

                return result is null
                    ? AvatarChangeResult.Failed("The server returned an empty response.")
                    : AvatarChangeResult.Ok(result.UpdatedUtc);
            }

            if (response.StatusCode is HttpStatusCode.RequestEntityTooLarge)
            {
                // The client downsizes first, so reaching this means the resize did not get
                // it small enough - worth saying plainly rather than as a bare 413.
                return AvatarChangeResult.Failed(
                    $"That picture is over {AvatarRules.MaxBytes / 1024} KB even after resizing.");
            }

            return AvatarChangeResult.Failed(await ReadProblemAsync(response, cancellationToken));
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            logger.LogWarning(ex, "Avatar upload failed");

            return AvatarChangeResult.Failed("Could not reach the server. Check your connection.");
        }
    }

    public async Task<AvatarChangeResult> DeleteAvatarAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.DeleteAsync(
                Endpoint("api/account/avatar"),
                cancellationToken);

            return response.IsSuccessStatusCode
                ? AvatarChangeResult.Ok()
                : AvatarChangeResult.Failed(await ReadProblemAsync(response, cancellationToken));
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            logger.LogWarning(ex, "Avatar removal failed");

            return AvatarChangeResult.Failed("Could not reach the server. Check your connection.");
        }
    }

    /// <summary>
    /// Whether an exception means the request did not get through, rather than that this app
    /// has a bug.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list is longer than it looks like it should be because
    /// <c>AddStandardResilienceHandler</c> wraps this client in a Polly pipeline, and that
    /// pipeline raises its own exception types rather than the <see cref="HttpRequestException"/>
    /// a bare <see cref="HttpClient"/> would. Catching only the HTTP ones lets the rest escape
    /// into an async command with nothing above it, which terminates the process.
    /// </para>
    /// <para>
    /// <see cref="TimeoutRejectedException"/> is the one that bit: an address with no route to
    /// it neither connects nor refuses, so the pipeline's total request timeout is what
    /// eventually fires. This never reproduces on the emulator, where a wrong address is
    /// refused immediately and cleanly - it needs a real phone pointed somewhere unroutable,
    /// which is exactly what "10.0.2.2" is once you leave the emulator.
    /// </para>
    /// <para>
    /// Deliberately a closed list rather than <c>catch (Exception)</c>. A genuine bug in here
    /// should still crash loudly in testing instead of being reported to the user as a
    /// network problem.
    /// </para>
    /// </remarks>
    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException
            or TaskCanceledException
            or TimeoutRejectedException
            or BrokenCircuitException;

    /// <summary>
    /// What to tell the user about a failure from <see cref="IsTransportFailure"/>.
    /// </summary>
    private static string Describe(Exception ex) => ex switch
    {
        // Overwhelmingly the most common self-hosted failure: a certificate Android does not
        // trust, because the reverse proxy uses a self-signed or private-CA cert. Worth naming
        // explicitly - the generic message sends people hunting the wrong problem entirely.
        HttpRequestException { InnerException: AuthenticationException } =>
            "Could not verify the server's HTTPS certificate. If it is self-signed, install its "
            + "certificate on this phone first.",

        // Nothing answered at all, as opposed to something answering with a refusal. Usually a
        // typo in the address, or a phone that is not on the same network as the server.
        TimeoutRejectedException or TaskCanceledException =>
            "The server took too long to answer. Check the address, and that this phone is on "
            + "the right network or VPN.",

        BrokenCircuitException =>
            "That address has failed repeatedly, so it is being left alone for a moment. Try "
            + "again shortly.",

        _ => "Could not reach that address. Check the server is running and that this phone is "
             + "on the right network or VPN."
    };

    private Uri Endpoint(string relativePath) =>
        serverSettings.BaseUrl is { } baseUrl
            ? new Uri(baseUrl, relativePath)
            : throw new InvalidOperationException(
                "No server address is configured. First-run setup must complete before any API call.");
}
