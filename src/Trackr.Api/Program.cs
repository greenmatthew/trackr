using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trackr.Api.Data;
using Trackr.Api.Endpoints;
using Trackr.Api.Identity;
using Trackr.Api.Security;

var builder = WebApplication.CreateBuilder(args);

// TLS is terminated upstream (nginx in front of us, and the user's own reverse
// proxy in front of that), so we speak plain HTTP and never redirect. Instead we
// trust the forwarded headers, which is what lets the Identity session cookie be
// marked Secure and what gives the rate limiter a real client IP.
// KnownProxies/KnownNetworks are cleared because container IPs on the compose
// network are not stable; the backend is not reachable from outside that network.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var connectionString = builder.Configuration.GetConnectionString("Trackr")
    ?? throw new InvalidOperationException(
        "No connection string named 'Trackr'. Set ConnectionStrings__Trackr in the environment.");

// Note the absence of EnableRetryOnFailure: a retrying execution strategy forbids
// user-initiated transactions, and invite redemption needs one. Startup instead
// retries around MigrateAsync - see DatabaseStartupExtensions.
builder.Services.AddDbContext<TrackrDbContext>(options => options.UseNpgsql(connectionString));

// The data-protection key ring encrypts the session cookie and every Identity token.
// By default it is a set of files under the container's home directory, which has no
// volume behind it and belongs to a non-root user - so it would be regenerated on every
// restart, silently signing everyone out and invalidating every pending password-reset
// link on each redeploy. A named volume does not help (Docker creates it root-owned, and
// the app quietly falls back to ephemeral keys). Postgres is already persistent, already
// holds the password hashes, and needs no compose change.
builder.Services.AddDataProtection()
    .SetApplicationName("Trackr")
    .PersistKeysToDbContext<TrackrDbContext>();

// Cookie authentication, not JWT (CLAUDE.md section 3). nginx serves the WASM app and
// proxies /api/ to us, so the app and the API are one origin and the session can be a
// cookie the browser handles itself. HttpOnly then means JavaScript cannot read it at
// all, which removes session theft via XSS.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies(cookies =>
    {
        // Ordering here is load-bearing. AddIdentityCookies has already ASSIGNED an
        // events object whose OnValidatePrincipal is the security-stamp validator (the
        // thing that ejects sessions after a password or 2FA change). Configure runs
        // afterwards and must MUTATE options.Events - assigning a fresh
        // CookieAuthenticationEvents would silently disable revocation, with no symptom
        // until the day it matters.
        cookies.ApplicationCookie!.Configure(options =>
        {
            Harden(options);

            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;

            // This is an API. The default 302 to /Account/Login turns a fetch() into a
            // confusing 200 plus a page of HTML; the Blazor client needs the status code.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // The short-lived cookie carrying "this user passed the password step and now
        // owes a 2FA code", plus the optional remember-this-browser cookie.
        cookies.TwoFactorUserIdCookie!.Configure(Harden);
        cookies.TwoFactorRememberMeCookie!.Configure(Harden);
        cookies.ExternalCookie!.Configure(Harden);

        void Harden(CookieAuthenticationOptions options)
        {
            options.Cookie.HttpOnly = true;

            // SameSite=Strict plus one origin plus JSON-only request bodies is the whole
            // CSRF story: a cross-origin HTML form cannot produce an application/json
            // body without a preflight the browser will refuse. No antiforgery tokens
            // are needed - please do not add them later without re-reading this.
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.IsEssential = true;

            // Every development path is plain HTTP by design: `dotnet watch` serves
            // http://localhost:5277 with no dev certificate, and the dev compose stack
            // serves http://localhost:8000 with nginx forwarding X-Forwarded-Proto: http.
            // CookieSecurePolicy.Always would make the browser silently discard the
            // cookie in both, so login would appear to succeed and then not stick. In
            // Production TLS always terminates at the reverse proxy, so Always is correct
            // and non-negotiable there.
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        }
    });

// AddIdentityCore rather than AddIdentity: the latter also wires up cookie schemes
// (already done above) and points DefaultSignInScheme at the external-login scheme, which
// is not how this app signs anyone in. AddIdentityApiEndpoints is not used either - it
// brings bearer tokens and MapIdentityApi's ungateable open /register.
builder.Services.AddIdentityCore<TrackrUser>(options =>
    {
        // Controls the width of the string key columns in the token and login tables.
        // It is read while the model is built, so it must be settled before the first
        // migration is generated - changing it later is a schema change.
        options.Stores.MaxLengthForKeys = 128;

        // Length beats composition rules (NIST SP 800-63B). Mandatory character classes
        // mostly push people towards "Password1!" and a sticky note.
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;

        // CLAUDE.md section 8.2 - the primary defence against password guessing.
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.User.RequireUniqueEmail = true;

        // There is no email infrastructure to confirm against, and registration is
        // already gated by first-run or a 256-bit invite token - that gate is the
        // confirmation.
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<TrackrDbContext>()
    // Registers the security-stamp validators that the application cookie's
    // OnValidatePrincipal depends on.
    .AddSignInManager()
    // Supplies the authenticator (TOTP) token provider behind 2FA, and the tokens behind
    // password reset.
    .AddDefaultTokenProviders();

// How quickly a password change, a 2FA change or a lockout ejects other sessions. The
// default is 30 minutes; a database round trip every 5 minutes per session is nothing at
// this scale.
builder.Services.Configure<SecurityStampValidatorOptions>(
    options => options.ValidationInterval = TimeSpan.FromMinutes(5));

// Fail safe: everything requires a signed-in user unless it explicitly opts out, so the
// endpoints added in milestone 3 and beyond are protected by default rather than by
// remembering to add an attribute.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddTrackrRateLimiting(builder.Configuration);

builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
var emailProvider = builder.Configuration
    .GetSection(EmailOptions.SectionName)
    .GetValue("Provider", EmailProvider.Log);
if (emailProvider is EmailProvider.Smtp)
{
    builder.Services.AddScoped<IEmailSender<TrackrUser>, SmtpEmailSender>();
}
else
{
    builder.Services.AddScoped<IEmailSender<TrackrUser>, LoggingEmailSender>();
}

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TrackrDbContext>("database");

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.MigrateDatabaseAsync();

app.UseForwardedHeaders();

#if DEBUG
// Dev convenience only: serve the Blazor app from this process so `dotnet watch`
// gives a single-origin dev server with hot reload, matching how nginx serves it
// in production. Compiled out of Release builds - see Trackr.Api.csproj.
// Ahead of routing, so static assets short-circuit and never meet the fallback policy.
if (app.Environment.IsDevelopment())
{
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();
}
#endif

// Routing, rate limiting and auth are called explicitly rather than left to the
// defaults, so the order below can be read at a glance.
app.UseRouting();

// Before authentication: reject a flood before spending cookie decryption and a database
// round trip on it.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    // AllowAnonymous because of the fallback policy above.
    app.MapOpenApi().AllowAnonymous();
}

app.MapHealthEndpoints();

// Anonymous for the same reason as the other health routes - see HealthEndpoints.
app.MapHealthChecks("/api/health/ready").AllowAnonymous();

app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapInviteEndpoints();

#if DEBUG
if (app.Environment.IsDevelopment())
{
    // AllowAnonymous is essential here: without it the fallback policy 401s index.html,
    // so a signed-out user could never load the app to reach the login page.
    app.MapFallbackToFile("index.html").AllowAnonymous();
}
#endif

app.Run();

/// <summary>
/// Exposes the implicitly generated Program class so the test project can drive the real
/// application through WebApplicationFactory&lt;Program&gt;.
/// </summary>
public partial class Program;
