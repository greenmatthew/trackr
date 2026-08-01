using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
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

        // --- native clients (Android) -------------------------------------------------
        // Bearer tokens rather than the cookie routes above. See TokenContracts for why 2FA
        // is one endpoint here and two on the web.

        app.MapPost("/api/auth/token", TokenAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("Token")
            .WithSummary("Password sign-in for a native client, returning bearer tokens.");

        app.MapPost("/api/auth/token/refresh", RefreshTokenAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitPolicies.Login)
            .WithName("RefreshToken")
            .WithSummary("Exchange a refresh token for a new access token.");

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

    /// <summary>
    /// Password sign-in for a native client, issuing bearer tokens instead of a cookie.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The password and 2FA steps are checked explicitly here rather than being delegated to
    /// <c>PasswordSignInAsync</c> + <c>TwoFactorAuthenticatorSignInAsync</c> the way
    /// <see cref="LoginAsync"/> does. That pair communicates between the two calls through
    /// Identity's <c>TwoFactorUserId</c> <b>cookie</b>: the first call writes it to the
    /// response, the second reads it from the next request. A native client posting once
    /// with the code already in hand never makes that round trip, so the handshake has
    /// nothing to read.
    /// </para>
    /// <para>
    /// Consequence to keep in mind: because this does not go through
    /// <c>TwoFactorAuthenticatorSignInAsync</c>, a wrong 2FA code does not feed the lockout
    /// counter automatically. <c>AccessFailedAsync</c> is called by hand below to keep the
    /// behaviour identical to the web path.
    /// </para>
    /// </remarks>
    private static async Task<IResult> TokenAsync(
        TokenRequest request,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Indistinguishable from a wrong password, so this cannot enumerate accounts.
            return TokenFailure(LoginStatus.Failed);
        }

        // Checks the password and counts the failure towards lockout, but does not sign
        // anyone in - the 2FA step below may still reject them. Resets the failed count on
        // success, so a correct password after four wrong ones clears the slate.
        var passwordResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (passwordResult.IsLockedOut)
        {
            return TokenFailure(LoginStatus.LockedOut, await userManager.GetLockoutEndDateAsync(user));
        }

        if (!passwordResult.Succeeded)
        {
            return TokenFailure(LoginStatus.Failed);
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            var twoFactorResult = await VerifySecondFactorAsync(request, user, userManager);
            if (twoFactorResult is not null)
            {
                return twoFactorResult;
            }
        }

        // Pointing the manager at the bearer scheme makes SignInAsync hand off to the bearer
        // handler, which serialises the access and refresh tokens straight into the response
        // body. That is why nothing is returned here: writing our own body as well would
        // append a second JSON document to the first.
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signInManager.SignInAsync(user, isPersistent: false);

        return Results.Empty;
    }

    /// <summary>
    /// Verifies the second factor. Returns null when it passed, or the failing result.
    /// </summary>
    private static async Task<IResult?> VerifySecondFactorAsync(
        TokenRequest request,
        TrackrUser user,
        UserManager<TrackrUser> userManager)
    {
        if (!string.IsNullOrWhiteSpace(request.TwoFactorCode))
        {
            // Authenticator apps and users both like to insert spaces or dashes.
            var code = request.TwoFactorCode.Replace(" ", "").Replace("-", "");

            var valid = await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                code);

            if (valid)
            {
                return null;
            }

            // Mirrors what TwoFactorAuthenticatorSignInAsync does internally, so 2FA guesses
            // feed the same lockout counter as password guesses.
            await userManager.AccessFailedAsync(user);

            return await userManager.IsLockedOutAsync(user)
                ? TokenFailure(LoginStatus.LockedOut, await userManager.GetLockoutEndDateAsync(user))
                : TokenFailure(LoginStatus.Failed);
        }

        if (!string.IsNullOrWhiteSpace(request.TwoFactorRecoveryCode))
        {
            // Spaces only. Identity issues recovery codes as "abcde-fghij" and compares them
            // verbatim, so stripping the dash the way a TOTP code needs would fail every
            // valid code.
            var recoveryCode = request.TwoFactorRecoveryCode.Replace(" ", "");

            var redeemed = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode);

            // Identity deliberately does not count a wrong recovery code towards lockout -
            // they are high-entropy, so guessing is not a realistic attack.
            return redeemed.Succeeded ? null : TokenFailure(LoginStatus.Failed);
        }

        // Password was right, but the account has an authenticator enrolled. The client is
        // expected to prompt and post the whole request again with a code.
        return TokenFailure(LoginStatus.RequiresTwoFactor);
    }

    /// <summary>
    /// Exchanges a refresh token for a fresh pair, so the app only asks for the password
    /// when the refresh token itself expires.
    /// </summary>
    private static async Task<IResult> RefreshTokenAsync(
        RefreshRequest request,
        SignInManager<TrackrUser> signInManager,
        IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
        TimeProvider timeProvider)
    {
        var protector = bearerTokenOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protector.Unprotect(request.RefreshToken);

        // ValidateSecurityStampAsync is the part that matters: it re-reads the user's
        // security stamp, so a password change, a 2FA change or a sign-out-everywhere
        // invalidates refresh tokens issued before it. Without it a stolen refresh token
        // would outlive the password it was obtained with.
        if (ticket?.Properties.ExpiresUtc is not { } expiresUtc
            || timeProvider.GetUtcNow() >= expiresUtc
            || await signInManager.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
        {
            return Results.Json(
                new TokenLoginResponse(LoginStatus.Failed),
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // As in TokenAsync, the bearer handler writes the new token pair itself.
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signInManager.SignInAsync(user, isPersistent: false);

        return Results.Empty;
    }

    private static IResult TokenFailure(LoginStatus status, DateTimeOffset? lockoutEnd = null) =>
        Results.Json(
            new TokenLoginResponse(status, lockoutEnd),
            statusCode: StatusCodes.Status401Unauthorized);

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
