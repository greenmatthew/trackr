using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Security;
using Trackr.Shared.Auth;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Sign-up, sign-in and password recovery.
/// </summary>
/// <remarks>
/// Written by hand over SignInManager and UserManager rather than using
/// MapIdentityApi&lt;T&gt;(), which exposes a /register that cannot be gated behind the
/// invite rule from CLAUDE.md section 8.4 and whose password-reset endpoints hard-require
/// an email sender. The algorithms that matter - hashing, TOTP, lockout counting - still
/// come entirely from Identity; only the routing and the policy around them are ours.
/// </remarks>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Routes are written out in full rather than via MapGroup so that the exact URL is
        // obvious at a glance, matching HealthEndpoints.

        app.MapGet("/api/auth/registration-status", GetRegistrationStatusAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("RegistrationStatus")
            .WithSummary("Whether the next account may be created freely or needs an invite.");

        app.MapPost("/api/auth/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("Register")
            .WithSummary("Create an account: the first one on an empty database, or one with an invite.");

        app.MapPost("/api/auth/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("Login")
            .WithSummary("Password sign-in. May report that a 2FA code is still owed.");

        app.MapPost("/api/auth/login/2fa", LoginTwoFactorAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("LoginTwoFactor")
            .WithSummary("Second step of sign-in: a code from the authenticator app.");

        app.MapPost("/api/auth/login/recovery-code", LoginRecoveryCodeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("LoginRecoveryCode")
            .WithSummary("Second step of sign-in using a single-use recovery code instead.");

        app.MapPost("/api/auth/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("Clear the session cookie.");

        app.MapGet("/api/auth/me", GetMeAsync)
            .WithName("Me")
            .WithSummary("Who the current session belongs to. The client's source of auth state.");

        app.MapPost("/api/auth/forgot-password", ForgotPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("ForgotPassword")
            .WithSummary("Send a password reset link. Always reports success.");

        app.MapPost("/api/auth/reset-password", ResetPasswordAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("ResetPassword")
            .WithSummary("Set a new password using a token from the reset link.");

        return app;
    }

    private static async Task<IResult> GetRegistrationStatusAsync(
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var anyUsers = await db.Users.AnyAsync(cancellationToken);

        return Results.Ok(new RegistrationStatusResponse(
            anyUsers ? RegistrationMode.InviteRequired : RegistrationMode.Bootstrap));
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        TrackrDbContext db,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager,
        CancellationToken cancellationToken)
    {
        // Creating the user and redeeming the invite must both happen or neither, or a
        // failed registration would burn a single-use token. Identity's store shares this
        // DbContext and auto-saves, so its SaveChanges enlists in this transaction too.
        // (This is why UseNpgsql must not enable a retrying execution strategy: those
        // forbid user-initiated transactions.)
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Accepted race: two simultaneous first-run registrations could both see an empty
        // table. The window is a single request on the first boot of a private server, and
        // closing it would mean locking the users table on every sign-up.
        var isBootstrap = !await db.Users.AnyAsync(cancellationToken);

        Invite? invite = null;
        if (!isBootstrap)
        {
            if (string.IsNullOrWhiteSpace(request.InviteToken))
            {
                return Results.Problem(
                    title: "Registration is closed.",
                    detail: "This server already has an account. Creating another needs an invite.",
                    statusCode: StatusCodes.Status403Forbidden,
                    extensions: new Dictionary<string, object?> { ["code"] = "registration_closed" });
            }

            var tokenHash = InviteTokens.Hash(request.InviteToken);
            invite = await db.Invites.FirstOrDefaultAsync(i => i.TokenHash == tokenHash, cancellationToken);

            // One message for every failure mode, so the endpoint cannot be used to probe
            // which tokens exist.
            if (invite is null
                || invite.RedeemedUtc is not null
                || invite.RevokedUtc is not null
                || invite.ExpiresUtc <= DateTimeOffset.UtcNow)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["inviteToken"] = ["That invite is not valid. It may have been used already, or expired."]
                });
            }
        }

        var user = new TrackrUser
        {
            UserName = request.Email,
            Email = request.Email,
            // Registration was already gated by first-run or a 256-bit token, and there is
            // no mail infrastructure to confirm against.
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        if (invite is not null)
        {
            invite.RedeemedUtc = DateTimeOffset.UtcNow;
            invite.RedeemedByUserId = user.Id;
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        // Signed in straight away: bootstrap means "first run on your own server" and an
        // invite means "you held a 256-bit secret". Making someone re-type the password
        // they chose two seconds ago is friction with no security value.
        await signInManager.SignInAsync(user, isPersistent: true);

        return Results.Ok(new MeResponse(user.Id, user.Email!, TwoFactorEnabled: false));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Same response as a wrong password, so this cannot be used to enumerate
            // accounts. There is no lockout to record against a user that does not exist.
            return Results.Json(
                new LoginResponse(LoginStatus.Failed),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // lockoutOnFailure is what actually counts failures towards the lockout configured
        // in Program.cs - CLAUDE.md section 8.2.
        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: request.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return Results.Ok(new LoginResponse(LoginStatus.Succeeded));
        }

        if (result.RequiresTwoFactor)
        {
            // Identity has issued the short-lived TwoFactorUserId cookie; the caller now
            // has a few minutes to post a code to /api/auth/login/2fa.
            return Results.Ok(new LoginResponse(LoginStatus.RequiresTwoFactor));
        }

        if (result.IsLockedOut)
        {
            return Results.Json(
                new LoginResponse(LoginStatus.LockedOut, await userManager.GetLockoutEndDateAsync(user)),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Json(
            new LoginResponse(LoginStatus.Failed),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LoginTwoFactorAsync(
        TwoFactorLoginRequest request,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            // No live password step. The TwoFactorUserId cookie is short-lived, so this is
            // usually someone leaving the code screen open too long rather than an attack.
            return Results.Json(
                new LoginResponse(LoginStatus.ChallengeExpired),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Authenticator apps and users both like to insert spaces or dashes.
        var code = request.Code.Replace(" ", "").Replace("-", "");

        // A wrong code here calls AccessFailedAsync internally, so 2FA attempts feed the
        // same lockout counter as passwords.
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            code,
            isPersistent: request.RememberMe,
            rememberClient: request.RememberMachine);

        if (result.Succeeded)
        {
            return Results.Ok(new LoginResponse(LoginStatus.Succeeded));
        }

        if (result.IsLockedOut)
        {
            return Results.Json(
                new LoginResponse(LoginStatus.LockedOut, await userManager.GetLockoutEndDateAsync(user)),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.Json(
            new LoginResponse(LoginStatus.Failed),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LoginRecoveryCodeAsync(
        RecoveryCodeLoginRequest request,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (user is null)
        {
            return Results.Json(
                new LoginResponse(LoginStatus.ChallengeExpired),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Spaces only. Identity issues recovery codes in the form "abcde-fghij" and
        // compares them verbatim, so stripping the dash the way we do for a TOTP code
        // would make every valid code fail.
        var code = request.RecoveryCode.Replace(" ", "");

        // Note that Identity deliberately does not count a wrong recovery code towards
        // lockout - they are high-entropy, so guessing is not a realistic attack.
        var result = await signInManager.TwoFactorRecoveryCodeSignInAsync(code);

        return result.Succeeded
            ? Results.Ok(new LoginResponse(LoginStatus.Succeeded))
            : Results.Json(
                new LoginResponse(LoginStatus.Failed),
                statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext httpContext,
        SignInManager<TrackrUser> signInManager)
    {
        // Clears the application, external and remember-this-browser cookies.
        await signInManager.SignOutAsync();

        // Not covered by SignOutAsync: a half-finished 2FA challenge would otherwise
        // survive a sign-out.
        await httpContext.SignOutAsync(IdentityConstants.TwoFactorUserIdScheme);

        return Results.NoContent();
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            // A valid cookie for an account that no longer exists.
            return Results.Unauthorized();
        }

        return Results.Ok(new MeResponse(user.Id, user.Email!, user.TwoFactorEnabled));
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        HttpContext httpContext,
        UserManager<TrackrUser> userManager,
        IEmailSender<TrackrUser> emailSender,
        IOptions<EmailOptions> emailOptions)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        // Always 202, whether or not the account exists, so this cannot be used to find
        // out which addresses are registered.
        if (user is not null)
        {
            var token = await userManager.GeneratePasswordResetTokenAsync(user);

            // The raw token contains characters that do not survive a query string.
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

            var baseUrl = BaseUrl(httpContext, emailOptions.Value);
            var link = $"{baseUrl}/reset-password?email={Uri.EscapeDataString(request.Email)}&code={code}";

            await emailSender.SendPasswordResetLinkAsync(user, request.Email, link);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        UserManager<TrackrUser> userManager)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return InvalidResetToken();
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
        }
        catch (FormatException)
        {
            // A mangled link, e.g. one wrapped across two lines by a mail client.
            return InvalidResetToken();
        }

        var result = await userManager.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        // Deliberately not signed in afterwards. With the default email provider this
        // token came out of a log file, so proving possession of the link should not by
        // itself hand over a live session - go through the normal login, including 2FA.
        return Results.NoContent();
    }

    private static IResult InvalidResetToken() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["code"] = ["That reset link is not valid or has expired. Request a new one."]
        });

    /// <summary>
    /// The origin to build links against. Derived from the request, which is correct once
    /// UseForwardedHeaders has applied the proxy's Host and X-Forwarded-Proto, and
    /// overridable for the case where it is not.
    /// </summary>
    private static string BaseUrl(HttpContext httpContext, EmailOptions options) =>
        !string.IsNullOrWhiteSpace(options.PublicBaseUrl)
            ? options.PublicBaseUrl.TrimEnd('/')
            : $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
}
