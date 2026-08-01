using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Trackr.Shared.Auth;

namespace Trackr.Client.Services;

/// <summary>
/// Supplies the app's authentication state by asking the server who the caller is.
/// </summary>
/// <remarks>
/// The session is an HttpOnly cookie (CLAUDE.md section 3), which means JavaScript - and
/// therefore this WebAssembly app - cannot read it at all. That is the entire point of the
/// cookie decision, and it is why this provider calls <c>GET /api/auth/me</c> rather than
/// decoding a token the way a JWT-based client would. The browser attaches the cookie by
/// itself: same-origin fetch defaults to <c>credentials: "same-origin"</c>, and nginx puts
/// the app and the API on one origin.
/// </remarks>
public sealed class CookieAuthenticationStateProvider(IServiceProvider services) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    /// <summary>
    /// The in-flight or completed lookup. Caching the Task rather than its result means
    /// several components rendering at startup share one request instead of racing.
    /// </summary>
    private Task<AuthenticationState>? _stateTask;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _stateTask ??= FetchAsync();

    /// <summary>
    /// Drops the cached state and re-reads it. Call after signing in or out, and whenever
    /// an API call comes back 401 - the session may have expired or been revoked
    /// elsewhere.
    /// </summary>
    public void Invalidate()
    {
        _stateTask = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private async Task<AuthenticationState> FetchAsync()
    {
        // Resolved lazily rather than injected: HttpClient's handler needs this provider,
        // so taking it as a constructor parameter would be a dependency cycle.
        var http = services.GetRequiredService<HttpClient>();

        try
        {
            using var response = await http.GetAsync("api/auth/me");
            if (!response.IsSuccessStatusCode)
            {
                return Anonymous;
            }

            var me = await response.Content.ReadFromJsonAsync<MeResponse>();
            if (me is null)
            {
                return Anonymous;
            }

            // The authentication type must be a non-empty string. With the parameterless
            // ClaimsIdentity constructor, IsAuthenticated stays false however many claims
            // are added, and AuthorizeView silently renders NotAuthorized for a user who
            // is in fact signed in.
            var identity = new ClaimsIdentity("Trackr.Cookie", ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, me.UserId.ToString()));
            identity.AddClaim(new Claim(ClaimTypes.Name, me.Email));
            identity.AddClaim(new Claim(ClaimTypes.Email, me.Email));

            return new AuthenticationState(new ClaimsPrincipal(identity));
        }
        catch (HttpRequestException)
        {
            // Offline, e.g. the installed PWA opened with no network. Treated as signed
            // out rather than crashing the app.
            return Anonymous;
        }
    }
}
