using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Security;
using Trackr.Api.Time;
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

    /// <summary>Long enough for a name, short enough not to be a paragraph in a greeting.</summary>
    private const int MaxDisplayNameLength = 60;

    /// <summary>
    /// Indented, unlike every other response here, because a person may well open this one.
    /// </summary>
    /// <remarks>
    /// Web defaults so the property names match the rest of the API, which is what makes the
    /// document readable next to <c>wiki/API-Reference.md</c>.
    /// </remarks>
    private static readonly JsonSerializerOptions ExportJson =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/account/profile", GetProfileAsync)
            .WithName("GetProfile")
            .WithSummary("The account's profile: name, units, body measurements and what they estimate.");

        app.MapPut("/api/account/profile", SaveProfileAsync)
            .WithName("SaveProfile")
            .WithSummary("Set the display name and the units numbers are shown in.");

        app.MapPut("/api/account/body", SaveBodyMetricsAsync)
            .WithName("SaveBodyMetrics")
            .WithSummary("Replace the body measurements. Sending nulls is how they are cleared.");

        app.MapPut("/api/account/timezone", SaveTimeZoneAsync)
            .WithName("SaveTimeZone")
            .WithSummary("Choose which zone this account's days are measured in.");

        app.MapPost("/api/account/email", RequestEmailChangeAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("RequestEmailChange")
            .WithSummary("Ask for the address to change. Sends a link; nothing moves until it is followed.");

        app.MapPost("/api/account/email/confirm", ConfirmEmailChangeAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("ConfirmEmailChange")
            .WithSummary("Follow the link, which is what actually changes the address.");

        app.MapGet("/api/account/export", ExportAsync)
            .WithName("ExportAccount")
            .WithSummary("Everything this account holds, as a JSON download.");

        app.MapDelete("/api/account", DeleteAccountAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("DeleteAccount")
            .WithSummary("Delete the account and everything in it. Not undoable.");

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

        app.MapGet("/api/account/avatar", GetAvatarAsync)
            .WithName("GetAvatar")
            .WithSummary("The profile picture. Supports If-None-Match; 404 when there is none.");

        app.MapPut("/api/account/avatar", SetAvatarAsync)
            .WithName("SetAvatar")
            .WithSummary("Replace the profile picture. Body is the raw image bytes.");

        app.MapDelete("/api/account/avatar", DeleteAvatarAsync)
            .WithName("DeleteAvatar")
            .WithSummary("Remove the profile picture, falling back to initials.");

        return app;
    }

    private static async Task<IResult> GetProfileAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        DayBoundary boundary)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        return Results.Ok(user.ToProfileResponse(boundary.TodayFor(user)));
    }

    /// <summary>
    /// Sets the display name and the units numbers are shown in.
    /// </summary>
    /// <remarks>
    /// <strong>The units are a display preference and change nothing the API returns.</strong>
    /// Every total, target and snapshot stays in kilocalories, and the client converts on the way
    /// to the screen - a stored number that changed meaning when a setting changed would rewrite
    /// what last month's chart said.
    /// </remarks>
    private static async Task<IResult> SaveProfileAsync(
        SaveProfileRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        DayBoundary boundary)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var errors = new ValidationErrors();

        var name = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();

        if (name is { Length: > MaxDisplayNameLength })
        {
            errors.Add("displayName", $"Keep it under {MaxDisplayNameLength} characters.");
        }

        // Checked rather than trusted to the deserialiser: the string form of an enum is rejected
        // when it is not a member, but a raw number is not, and (EnergyUnit)7 would be stored as
        // the text "7" and read back as nothing at all.
        RequireDefined(request.EnergyUnit, "energyUnit", errors);
        RequireDefined(request.MeasurementSystem, "measurementSystem", errors);

        if (errors.Any)
        {
            return errors.Problem();
        }

        user.DisplayName = name;
        user.EnergyUnit = request.EnergyUnit;
        user.MeasurementSystem = request.MeasurementSystem;

        var result = await userManager.UpdateAsync(user);

        return result.Succeeded
            ? Results.Ok(user.ToProfileResponse(boundary.TodayFor(user)))
            : result.ToValidationProblem();
    }

    /// <summary>
    /// Replaces the body measurements, and re-derives what they estimate.
    /// </summary>
    /// <remarks>
    /// Wholesale, so that clearing one is a request rather than a missing feature: a merge cannot
    /// tell "leave my weight alone" from "I no longer want a weight stored", and this is exactly
    /// the data somebody may want gone.
    /// <para>
    /// The bounds are wide because they catch a slipped decimal point rather than an unusual body.
    /// A birth date in the future is refused outright, since it is a typo with no reading that
    /// makes sense; an age that is merely young is accepted and simply yields no estimate, because
    /// the equation is not the right one for children and refusing the profile over it would be a
    /// stranger answer than declining to guess.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SaveBodyMetricsAsync(
        SaveBodyMetricsRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        DayBoundary boundary)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var today = boundary.TodayFor(user);
        var errors = new ValidationErrors();

        RequireDefined(request.Sex, "sex", errors);
        RequireDefined(request.ActivityLevel, "activityLevel", errors);

        RequireWithin(
            request.HeightCm,
            (decimal)BodyMetricLimits.MinHeightCm,
            (decimal)BodyMetricLimits.MaxHeightCm,
            "heightCm",
            "a height in centimetres",
            errors);

        RequireWithin(
            request.WeightKg,
            (decimal)BodyMetricLimits.MinWeightKg,
            (decimal)BodyMetricLimits.MaxWeightKg,
            "weightKg",
            "a weight in kilograms",
            errors);

        if (request.BirthDate is { } born)
        {
            if (born > today)
            {
                errors.Add("birthDate", "That date has not happened yet.");
            }
            else if (born < today.AddYears(-BodyMetricLimits.MaxAgeYears))
            {
                errors.Add("birthDate", "That is further back than a birth date goes. Check the year.");
            }
        }

        if (errors.Any)
        {
            return errors.Problem();
        }

        user.Sex = request.Sex;
        user.BirthDate = request.BirthDate;

        // Rounded to what the column keeps, so the response to a write is what was written rather
        // than what was sent - the same rule every stored amount in this codebase follows.
        user.HeightCm = request.HeightCm is { } height ? StoredPrecision.Body(height) : null;
        user.WeightKg = request.WeightKg is { } weight ? StoredPrecision.Body(weight) : null;

        user.ActivityLevel = request.ActivityLevel;

        var result = await userManager.UpdateAsync(user);

        return result.Succeeded
            ? Results.Ok(user.ToProfileResponse(today))
            : result.ToValidationProblem();
    }

    private static void RequireDefined<TEnum>(TEnum value, string field, ValidationErrors errors)
        where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            errors.Add(field, "That is not one of the choices.");
        }
    }

    private static void RequireWithin(
        decimal? value,
        decimal minimum,
        decimal maximum,
        string field,
        string description,
        ValidationErrors errors)
    {
        if (value is { } amount && (amount < minimum || amount > maximum))
        {
            errors.Add(
                field,
                $"That is not {description} - it should be between {minimum:0} and {maximum:0}.");
        }
    }

    /// <summary>
    /// Sets the zone this account's days are measured in.
    /// </summary>
    /// <remarks>
    /// Stored on the account rather than sent with each request, because the server aggregates -
    /// CLAUDE.md section 9.13. A total has to agree with itself between the chat, the stats views
    /// and the goals, and one client being in an airport must not move somebody's midnight.
    /// <para>
    /// Validated here, against this server's own zone database, which is the only one that
    /// matters: a zone the phone knows and the server does not would silently become UTC, and the
    /// day would quietly run on the wrong clock.
    /// </para>
    /// </remarks>
    private static async Task<IResult> SaveTimeZoneAsync(
        SaveTimeZoneRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var id = string.IsNullOrWhiteSpace(request.TimeZoneId) ? null : request.TimeZoneId.Trim();

        if (!DayBoundary.IsKnownZone(id))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["timeZoneId"] = ["This server does not recognise that time zone."]
            });
        }

        user.TimeZoneId = id;

        var result = await userManager.UpdateAsync(user);

        return result.Succeeded
            ? Results.Ok(user.ToMeResponse())
            : Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["timeZoneId"] = [.. result.Errors.Select(error => error.Description)]
            });
    }

    /// <summary>
    /// Starts an email change by sending a link to the <em>new</em> address.
    /// </summary>
    /// <remarks>
    /// <strong>Nothing changes here.</strong> The email address is the login and the only way back
    /// in through a password reset, so applying a typo immediately would lock somebody out of
    /// their own server with no route back. Proving the new address works is the whole point, and
    /// it is the same shape as the password-reset flow this borrows from - including the default
    /// provider, which writes the link to the backend log.
    /// <para>
    /// The password is re-checked because a session left open on a shared laptop should not be
    /// enough to move an account somewhere else, which is the same reason turning 2FA off asks.
    /// </para>
    /// </remarks>
    private static async Task<IResult> RequestEmailChangeAsync(
        ChangeEmailRequest request,
        HttpContext httpContext,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        IEmailSender<TrackrUser> emailSender,
        IOptions<EmailOptions> emailOptions)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["That password is not right."]
            });
        }

        var newEmail = request.NewEmail.Trim();

        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newEmail"] = ["That is already this account's address."]
            });
        }

        // Told plainly rather than hidden behind a generic 202. Unlike the forgot-password flow,
        // the caller here is already signed in and can see the invite list, so refusing to say
        // "that address is taken" would hide nothing and would instead send them a link that
        // cannot work.
        if (await userManager.FindByEmailAsync(newEmail) is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["newEmail"] = ["Another account on this server already uses that address."]
            });
        }

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);

        // The raw token contains characters that do not survive a query string.
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        var baseUrl = !string.IsNullOrWhiteSpace(emailOptions.Value.PublicBaseUrl)
            ? emailOptions.Value.PublicBaseUrl.TrimEnd('/')
            : $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        var link = $"{baseUrl}/change-email?email={Uri.EscapeDataString(newEmail)}&code={code}";

        // To the new address, never the old one: the point is to prove that somebody can read mail
        // there before the account depends on it.
        await emailSender.SendConfirmationLinkAsync(user, newEmail, link);

        return Results.Accepted();
    }

    /// <summary>
    /// Applies the change, once the link proves the new address was reachable.
    /// </summary>
    /// <remarks>
    /// Signed in as well as holding the token, deliberately. With the default provider the link
    /// came out of a log file, and possession of it alone should not be able to move somebody
    /// else's account - the same reasoning that stops a password reset signing the caller in.
    /// <para>
    /// The user name moves with the address, because this application logs in by email and Identity
    /// keeps the two as separate columns. Changing one without the other leaves an account that
    /// reports the new address and only accepts the old one at the login screen.
    /// </para>
    /// </remarks>
    private static async Task<IResult> ConfirmEmailChangeAsync(
        ConfirmEmailChangeRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        string token;

        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code));
        }
        catch (FormatException)
        {
            // A mangled link, e.g. one wrapped across two lines by a mail client.
            return InvalidEmailToken();
        }

        var newEmail = request.NewEmail.Trim();

        var result = await userManager.ChangeEmailAsync(user, newEmail, token);
        if (!result.Succeeded)
        {
            return InvalidEmailToken();
        }

        var renamed = await userManager.SetUserNameAsync(user, newEmail);
        if (!renamed.Succeeded)
        {
            return renamed.ToValidationProblem();
        }

        // Both writes rolled the security stamp. Without this the caller is signed out moments
        // after successfully changing their address.
        await signInManager.RefreshSignInAsync(user);

        return Results.Ok(user.ToMeResponse());
    }

    /// <summary>
    /// Everything this account holds, as a JSON file.
    /// </summary>
    /// <remarks>
    /// A download rather than a rendered page: the point is to have the data somewhere Trackr is
    /// not. <see cref="AccountExport"/> documents what is in it and what deliberately is not.
    /// </remarks>
    private static async Task<IResult> ExportAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        DayBoundary boundary,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var export = await AccountExport.ForAsync(user, db, time.GetUtcNow(), cancellationToken);

        // Named for the account's own day, since that is the day the totals inside it are grouped
        // by - a file stamped with a date the data does not use would be its own small confusion.
        var fileName = $"trackr-export-{boundary.TodayFor(user):yyyy-MM-dd}.json";

        return Results.File(
            JsonSerializer.SerializeToUtf8Bytes(export, ExportJson),
            "application/json",
            fileName);
    }

    /// <summary>
    /// Deletes the account and everything belonging to it.
    /// </summary>
    /// <remarks>
    /// The cascades in <see cref="TrackrDbContext"/> do most of it: the avatar, the goals, the log
    /// and its snapshots, the meal photographs, and the catalog items this account owns all go with
    /// the row. <strong>Global catalog items do not</strong> - they belong to the server and other
    /// people's recipes are made of them, which is the same rule the export follows.
    /// <para>
    /// Invites are the one thing handled by hand. Their foreign keys null rather than cascade, so
    /// the record of who invited whom survives the person - but an invite this account minted and
    /// nobody redeemed is a live credential, and one of those must not outlive its issuer. Those
    /// are revoked first.
    /// </para>
    /// <para>
    /// Nothing here refuses to delete the last account on the server. It reopens registration, the
    /// way an empty database does, and the alternative is telling somebody they may not leave their
    /// own machine.
    /// </para>
    /// </remarks>
    private static async Task<IResult> DeleteAccountAsync(
        // Explicit, because minimal APIs will not infer a body on DELETE - the verb usually
        // carries everything it needs in the route. This one needs credentials, and asking for
        // them in a query string would put a password in every access log between here and the
        // browser.
        [FromBody] DeleteAccountRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        SignInManager<TrackrUser> signInManager,
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["password"] = ["That password is not right."]
            });
        }

        // Typed by hand, so this cannot be reached by a stray tap on a button.
        if (!string.Equals(
                request.Confirmation.Trim(),
                DeleteAccountRequest.RequiredConfirmation,
                StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["confirmation"] =
                    [$"Type {DeleteAccountRequest.RequiredConfirmation} to confirm."]
            });
        }

        var now = Timestamps.UtcNow();

        var outstanding = await db.Invites
            .Where(invite => invite.CreatedByUserId == user.Id
                && invite.RedeemedUtc == null
                && invite.RevokedUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var invite in outstanding)
        {
            invite.RevokedUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            return result.ToValidationProblem();
        }

        // The session belongs to an account that no longer exists. Ending it here means the client
        // is not left holding a cookie whose every request now 401s for a reason it cannot see.
        await signInManager.SignOutAsync();

        return Results.NoContent();
    }

    private static IResult InvalidEmailToken() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["code"] = ["That link is not valid or has expired. Ask for a new one."]
        });

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

    private static async Task<IResult> GetAvatarAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var avatar = await db.UserAvatars
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.UserId == user.Id, cancellationToken);

        if (avatar is null)
        {
            // Not an error. Having no picture is the default, and the client draws initials.
            return Results.NotFound();
        }

        // Results.Bytes handles If-None-Match itself and answers 304 when it matches, which
        // is the whole point of sending the tag: the phone re-checks on every launch and
        // almost always gets a few bytes of headers back instead of the image.
        return Results.Bytes(
            avatar.Content,
            avatar.ContentType,
            entityTag: ETagFor(avatar.UpdatedUtc));
    }

    private static async Task<IResult> SetAvatarAsync(
        HttpRequest request,
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        // Strip any "; charset=" - a client that sends one is not wrong, and rejecting the
        // upload over it would be a baffling failure.
        var contentType = request.ContentType?.Split(';')[0].Trim();

        if (!AvatarRules.IsAllowedContentType(contentType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["contentType"] =
                [
                    "That image format is not supported. Use "
                        + string.Join(", ", AvatarRules.AllowedContentTypes) + "."
                ]
            });
        }

        // Content-Length is a claim, not a fact, so it is only a fast rejection - the read
        // below is what actually enforces the cap.
        if (request.ContentLength > AvatarRules.MaxBytes)
        {
            return TooLarge();
        }

        var content = await ReadCappedAsync(request.Body, AvatarRules.MaxBytes, cancellationToken);
        if (content is null)
        {
            return TooLarge();
        }

        if (content.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["content"] = ["The image was empty."]
            });
        }

        // Truncated to what Postgres keeps, because this timestamp is a cache marker: the client
        // stores what the upload returned and compares it against what /me reports afterwards.
        // Timestamps has the full reasoning, and the log entities need the identical treatment.
        var now = Timestamps.UtcNow();

        var avatar = await db.UserAvatars
            .FirstOrDefaultAsync(a => a.UserId == user.Id, cancellationToken);

        if (avatar is null)
        {
            avatar = new UserAvatar { UserId = user.Id };
            db.UserAvatars.Add(avatar);
        }

        avatar.Content = content;
        avatar.ContentType = contentType!;
        avatar.UpdatedUtc = now;

        // The marker on the user row is what /me reports and what every client caches
        // against, so the two must move together or a stale picture is never re-fetched.
        user.AvatarUpdatedUtc = now;
        db.Update(user);

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new AvatarResponse(now));
    }

    private static async Task<IResult> DeleteAvatarAsync(
        ClaimsPrincipal principal,
        UserManager<TrackrUser> userManager,
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var avatar = await db.UserAvatars
            .FirstOrDefaultAsync(a => a.UserId == user.Id, cancellationToken);

        if (avatar is not null)
        {
            db.UserAvatars.Remove(avatar);
        }

        user.AvatarUpdatedUtc = null;
        db.Update(user);

        await db.SaveChangesAsync(cancellationToken);

        // Idempotent: deleting a picture that was not there is a success, not a 404. The
        // caller wanted no picture and there is no picture.
        return Results.NoContent();
    }

    private static EntityTagHeaderValue ETagFor(DateTimeOffset updatedUtc) =>
        // Ticks rather than a hash of the bytes: it is already unique per write, needs no
        // second pass over the content, and re-uploading the same image still counts as a
        // change - which is what the user just asked for.
        new($"\"{updatedUtc.UtcTicks}\"");

    private static IResult TooLarge() =>
        Results.Problem(
            title: "Image too large",
            detail: $"Profile pictures must be under {AvatarRules.MaxBytes / 1024} KB.",
            statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>
    /// Reads the body, giving up as soon as it exceeds <paramref name="maxBytes"/>.
    /// </summary>
    /// <returns>The bytes, or null if the body was longer than the cap.</returns>
    /// <remarks>
    /// Reading into a capped buffer rather than calling ToArrayAsync on the stream, because
    /// the latter allocates whatever the caller sends. The endpoint is authenticated, so this
    /// is not much of an attack surface, but "an authenticated user can make the server
    /// allocate arbitrary memory" is still not a sentence worth being true.
    /// </remarks>
    private static async Task<byte[]?> ReadCappedAsync(
        Stream body,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        var chunk = new byte[8192];
        int read;

        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
