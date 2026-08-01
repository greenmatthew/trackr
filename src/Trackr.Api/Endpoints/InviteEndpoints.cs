using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Security;
using Trackr.Shared.Auth;

namespace Trackr.Api.Endpoints;

/// <summary>
/// Minting and managing registration invites.
/// </summary>
/// <remarks>
/// This is how a household member gets an account without reopening public sign-up
/// (CLAUDE.md section 8.4). Any signed-in user may issue one; with a single-user server
/// that is the owner, and restricting it further is a later data change now that roles are
/// mapped.
/// </remarks>
public static class InviteEndpoints
{
    private const int MinExpiryHours = 1;
    private const int MaxExpiryHours = 720;

    public static IEndpointRouteBuilder MapInviteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/invites", CreateInviteAsync)
            .RequireRateLimiting(RateLimitPolicies.Sensitive)
            .WithName("CreateInvite")
            .WithSummary("Mint a single-use registration invite. The token is returned once.");

        app.MapGet("/api/invites", ListInvitesAsync)
            .WithName("ListInvites")
            .WithSummary("All invites and their current state.");

        app.MapDelete("/api/invites/{id:guid}", RevokeInviteAsync)
            .WithName("RevokeInvite")
            .WithSummary("Revoke an unused invite.");

        return app;
    }

    private static async Task<IResult> CreateInviteAsync(
        CreateInviteRequest request,
        HttpContext httpContext,
        ClaimsPrincipal principal,
        TrackrDbContext db,
        UserManager<TrackrUser> userManager,
        IOptions<EmailOptions> emailOptions,
        CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var token = InviteTokens.Create();
        var expiresUtc = DateTimeOffset.UtcNow.AddHours(
            Math.Clamp(request.ExpiresInHours, MinExpiryHours, MaxExpiryHours));

        var invite = new Invite
        {
            TokenHash = InviteTokens.Hash(token),
            TokenPrefix = InviteTokens.Prefix(token),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedByUserId = user.Id,
            CreatedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = expiresUtc
        };

        db.Invites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);

        var baseUrl = !string.IsNullOrWhiteSpace(emailOptions.Value.PublicBaseUrl)
            ? emailOptions.Value.PublicBaseUrl.TrimEnd('/')
            : $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        return Results.Created(
            $"/api/invites/{invite.Id}",
            new InviteCreatedResponse(
                invite.Id,
                token,
                $"{baseUrl}/register?token={Uri.EscapeDataString(token)}",
                invite.ExpiresUtc));
    }

    private static async Task<IResult> ListInvitesAsync(
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var invites = await db.Invites
            .AsNoTracking()
            .Include(invite => invite.RedeemedBy)
            .OrderByDescending(invite => invite.CreatedUtc)
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        return Results.Ok(invites.Select(invite => new InviteResponse(
            invite.Id,
            invite.TokenPrefix,
            invite.Note,
            StatusOf(invite, now),
            invite.CreatedUtc,
            invite.ExpiresUtc,
            invite.RedeemedUtc,
            invite.RedeemedBy?.Email)).ToArray());
    }

    private static async Task<IResult> RevokeInviteAsync(
        Guid id,
        TrackrDbContext db,
        CancellationToken cancellationToken)
    {
        var invite = await db.Invites.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invite is null)
        {
            return Results.NotFound();
        }

        if (invite.RedeemedUtc is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [""] = ["That invite has already been used, so there is nothing to revoke."]
            });
        }

        // Soft revoke, so the record of who invited whom survives.
        invite.RevokedUtc ??= DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return Results.NoContent();
    }

    /// <summary>
    /// Redeemed beats revoked beats expired: an invite that was used is reported as used
    /// even if its expiry has since passed.
    /// </summary>
    private static InviteStatus StatusOf(Invite invite, DateTimeOffset now) => invite switch
    {
        { RedeemedUtc: not null } => InviteStatus.Redeemed,
        { RevokedUtc: not null } => InviteStatus.Revoked,
        _ when invite.ExpiresUtc <= now => InviteStatus.Expired,
        _ => InviteStatus.Active
    };
}
