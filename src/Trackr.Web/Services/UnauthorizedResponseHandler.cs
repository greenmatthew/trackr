namespace Trackr.Web.Services;

/// <summary>
/// Notices when the server stops recognising our session and updates the app's auth state
/// to match.
/// </summary>
/// <remarks>
/// Without this, an expired or revoked cookie would leave the UI still rendering as signed
/// in while every request quietly failed. Invalidating the provider makes AuthorizeRouteView
/// re-evaluate and send the user to the login page instead.
/// </remarks>
public sealed class UnauthorizedResponseHandler(IServiceProvider services) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized && ShouldInvalidate(request))
        {
            // Resolved here rather than injected, because the provider resolves HttpClient
            // - which is this handler - and constructor injection would be a cycle.
            services.GetRequiredService<CookieAuthenticationStateProvider>().Invalidate();
        }

        return response;
    }

    /// <summary>
    /// Whether a 401 from this request means "the session we thought we had is gone".
    /// </summary>
    /// <remarks>
    /// Everything under <c>/api/auth/</c> is excluded, and the exclusion is load-bearing
    /// rather than an optimisation. A 401 from <c>/api/auth/me</c> is the answer to the
    /// provider's own question, so invalidating on it would clear the cache, re-ask, get
    /// another 401, and loop forever - starving the UI while it spins. The rest of that
    /// path is excluded on the same principle: a 401 from <c>login</c> means the password
    /// was wrong, not that a session expired, and <see cref="AuthClient"/> already
    /// invalidates explicitly after a login, logout or registration.
    /// </remarks>
    private static bool ShouldInvalidate(HttpRequestMessage request) =>
        request.RequestUri is null
        || !request.RequestUri.AbsolutePath.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase);
}
