using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Trackr.Api.Identity;
using Trackr.Api.Security;
using Trackr.Shared.Auth;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Settings for the signed-in account: password changes and two-factor authentication.
/// </summary>
/// <remarks>
/// Every route here requires a session - they are covered by the fallback authorization
/// policy in Program.cs rather than by individual attributes.
/// <para>
/// Note the RefreshSignInAsync call after each state change. ChangePasswordAsync,
/// SetTwoFactorEnabledAsync and ResetAuthenticatorKeyAsync all roll the user's security
/// stamp, which is exactly how other sessions get ejected - but without a refresh the
/// caller's own cookie is stale too, and the security-stamp validator would sign them out
/// moments after they successfully changed a setting.
/// </para>
/// </remarks>
public static class AccountEndpoints
{
    /// <summary>
    /// Recovery codes issued when 2FA is enabled. Ten is Identity's own default and is
    /// enough to print once and keep somewhere safe.
    /// </summary>
    private const int RecoveryCodeCount = 10;

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/account/password", ChangePasswordAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("ChangePassword")
            .WithSummary("Change the password, re-checking the current one first.");

        app.MapGet("/api/account/2fa", GetTwoFactorStatusAsync)
            .WithName("TwoFactorStatus")
            .WithSummary("Whether 2FA is on, and how many recovery codes remain.");

        app.MapPost("/api/account/2fa/enroll", EnrollTwoFactorAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("TwoFactorEnroll")
            .WithSummary("Start enrolment: returns the shared secret and a QR code to scan.");

        app.MapPost("/api/account/2fa/enable", EnableTwoFactorAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("TwoFactorEnable")
            .WithSummary("Finish enrolment by proving the authenticator works. Returns recovery codes once.");

        app.MapPost("/api/account/2fa/disable", DisableTwoFactorAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("TwoFactorDisable")
            .WithSummary("Turn 2FA off. Requires the account password.");

        app.MapPost("/api/account/2fa/recovery-codes", RegenerateRecoveryCodesAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("TwoFactorRecoveryCodes")
            .WithSummary("Replace the recovery codes. Returns the new set once.");

        return app;
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var result = await userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword);

        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        // The password change rolled the security stamp, invalidating every session
        // including this one. Re-issue this caller's cookie so they stay signed in while
        // everyone else is ejected.
        await signInManager.RefreshSignInAsync(user);

        return Results.NoContent();
    }

    private static async Task<IResult> GetTwoFactorStatusAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(new TwoFactorStatusResponse(
            IsEnabled: await userManager.GetTwoFactorEnabledAsync(user),
            HasAuthenticatorKey: !string.IsNullOrEmpty(await userManager.GetAuthenticatorKeyAsync(user)),
            RecoveryCodesLeft: await userManager.CountRecoveryCodesAsync(user),
            IsMachineRemembered: await signInManager.IsTwoFactorClientRememberedAsync(user)));
    }

    private static async Task<IResult> EnrollTwoFactorAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (await userManager.GetTwoFactorEnabledAsync(user))
        {
            // Re-enrolling would silently invalidate the authenticator entry that is
            // currently working. Disable first, deliberately.
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Two-factor authentication is already on. Turn it off first to enrol a new device."]
            });
        }

        var key = await userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            // Only reset when there is nothing to reset. Otherwise reloading the settings
            // page mid-enrolment would invalidate the QR code already scanned, and the
            // codes from the authenticator app would start being rejected for no visible
            // reason.
            await userManager.ResetAuthenticatorKeyAsync(user);
            await signInManager.RefreshSignInAsync(user);
            key = await userManager.GetAuthenticatorKeyAsync(user);
        }

        var uri = AuthenticatorQrCode.BuildUri(user.Email!, key!);

        return Results.Ok(new TwoFactorEnrollmentResponse(
            SharedKey: AuthenticatorQrCode.FormatKey(key!),
            AuthenticatorUri: uri,
            QrCodeSvgDataUri: AuthenticatorQrCode.BuildSvgDataUri(uri)));
    }

    private static async Task<IResult> EnableTwoFactorAsync(
        TwoFactorCodeRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var code = request.Code.Replace(" ", "").Replace("-", "");

        // CLAUDE.md section 8.1: confirm enrolment by having the user type one valid code,
        // so 2FA is never switched on for a device that cannot actually generate codes.
        var isValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);

        if (!isValid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["code"] = ["That code is not right. Check the app and try the current code."]
            });
        }

        await userManager.SetTwoFactorEnabledAsync(user, true);

        // Issued once, here. Only hashes are stored, so they can never be shown again -
        // the client must make that clear.
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

        await signInManager.RefreshSignInAsync(user);

        return Results.Ok(new RecoveryCodesResponse(recoveryCodes?.ToArray() ?? []));
    }

    private static async Task<IResult> DisableTwoFactorAsync(
        DisableTwoFactorRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Turning 2FA off weakens the account, so it is worth re-proving who is at the
        // keyboard rather than relying on a session cookie someone walked away from.
        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["That password is not right."]
            });
        }

        await userManager.SetTwoFactorEnabledAsync(user, false);

        // Clear the shared secret too, so re-enrolling later starts fresh rather than
        // silently reusing a secret that may have been captured.
        await userManager.ResetAuthenticatorKeyAsync(user);
        await signInManager.ForgetTwoFactorClientAsync();
        await signInManager.RefreshSignInAsync(user);

        return Results.NoContent();
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["Recovery codes only apply once two-factor authentication is on."]
            });
        }

        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

        return Results.Ok(new RecoveryCodesResponse(recoveryCodes?.ToArray() ?? []));
    }
}
