using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackr.Api.Data;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The account self-service milestone 2 left out and section 9.13 asks for: changing the address,
/// taking the data away, and leaving.
/// </summary>
/// <remarks>
/// The rule behind most of these is that none of the three may be reachable by accident. The
/// address is the login and the only route back in through a password reset; the delete is not
/// undoable. Each re-checks the password, and the delete also wants a word typed.
/// </remarks>
public sealed class AccountSelfServiceTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    private const string NewEmail = "somewhere.else@example.test";

    /// <summary>
    /// Asking changes nothing at all until the link is followed.
    /// </summary>
    /// <remarks>
    /// The whole reason this is two steps. Applying a typo immediately would lock somebody out of
    /// their own server with no way back, because the address they can no longer read is where a
    /// password reset would be sent.
    /// </remarks>
    [Fact]
    public async Task Asking_to_change_the_address_does_not_change_it()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PostAsJsonAsync("/api/account/email", new ChangeEmailRequest
        {
            Password = OwnerPassword,
            NewEmail = NewEmail
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(OwnerEmail, (await MeAsync(client)).Email);

        // And the old address still signs in, which is the part that matters.
        using var loginClient = Factory.NewClient();
        Assert.Equal(LoginStatus.Succeeded, (await LoginBodyAsync(loginClient, OwnerEmail, OwnerPassword)).Status);
    }

    /// <remarks>
    /// The user name moves with the address. This application logs in by email and Identity keeps
    /// the two as separate columns, so changing one alone leaves an account that reports the new
    /// address and only accepts the old one at the login screen - which is the bug this asserts is
    /// not there.
    /// </remarks>
    [Fact]
    public async Task Following_the_link_moves_the_address_and_the_login_with_it()
    {
        using var client = await RegisterOwnerAsync();

        var code = await Factory.GenerateEmailChangeCodeAsync(OwnerEmail, NewEmail);

        using var confirmed = await client.PostAsJsonAsync(
            "/api/account/email/confirm",
            new ConfirmEmailChangeRequest { NewEmail = NewEmail, Code = code });

        confirmed.EnsureSuccessStatusCode();

        Assert.Equal(NewEmail, (await MeAsync(client)).Email);

        using var withNew = Factory.NewClient();
        Assert.Equal(LoginStatus.Succeeded, (await LoginBodyAsync(withNew, NewEmail, OwnerPassword)).Status);

        using var withOld = Factory.NewClient();
        Assert.Equal(LoginStatus.Failed, (await LoginBodyAsync(withOld, OwnerEmail, OwnerPassword)).Status);
    }

    /// <remarks>
    /// The token is bound to the new address as well as to the account, so a link cannot be
    /// re-aimed at somewhere else by editing the query string.
    /// </remarks>
    [Fact]
    public async Task A_code_issued_for_one_address_does_not_work_for_another()
    {
        using var client = await RegisterOwnerAsync();

        var code = await Factory.GenerateEmailChangeCodeAsync(OwnerEmail, NewEmail);

        using var response = await client.PostAsJsonAsync(
            "/api/account/email/confirm",
            new ConfirmEmailChangeRequest { NewEmail = "third.party@example.test", Code = code });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OwnerEmail, (await MeAsync(client)).Email);
    }

    [Fact]
    public async Task Changing_the_address_re_checks_the_password()
    {
        using var client = await RegisterOwnerAsync();

        using var response = await client.PostAsJsonAsync("/api/account/email", new ChangeEmailRequest
        {
            Password = "not the password",
            NewEmail = NewEmail
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("password", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// Said plainly rather than hidden behind a generic 202. The caller is signed in and can see
    /// the invite list, so refusing to say would hide nothing and would instead send a link that
    /// cannot work.
    /// </remarks>
    [Fact]
    public async Task An_address_another_account_already_uses_is_refused()
    {
        using var owner = await RegisterOwnerAsync();
        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var response = await owner.PostAsJsonAsync("/api/account/email", new ChangeEmailRequest
        {
            Password = OwnerPassword,
            NewEmail = "member@example.test"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("newEmail", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The export carries the log, the personal catalog and the profile - and names the photos
    /// rather than embedding them.
    /// </summary>
    /// <remarks>
    /// A few hundred meals of base64 would make a document nothing can open, for bytes that are
    /// already downloadable one at a time. Saying where they are is honest; leaving them out
    /// silently would not be.
    /// </remarks>
    [Fact]
    public async Task The_export_carries_the_account_and_points_at_the_photographs()
    {
        using var client = await RegisterOwnerAsync();

        using var logged = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.AdHocLog(name: "Porridge", energyKcal: 320m));
        logged.EnsureSuccessStatusCode();

        using var response = await client.GetAsync("/api/account/export");

        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("trackr-export-", response.Content.Headers.ContentDisposition?.FileName ?? "", StringComparison.Ordinal);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        Assert.Equal(OwnerEmail, root.GetProperty("account").GetProperty("email").GetString());

        var entry = Assert.Single(root.GetProperty("log").EnumerateArray().ToArray());
        var item = Assert.Single(entry.GetProperty("items").EnumerateArray().ToArray());

        Assert.Equal("Porridge", item.GetProperty("name").GetString());
        Assert.Equal(320m, item.GetProperty("energyKcal").GetDecimal());

        // The manifest exists even when it is empty, so nobody has to wonder whether photographs
        // were omitted or simply never taken.
        Assert.Empty(root.GetProperty("photos").EnumerateArray().ToArray());
    }

    /// <remarks>
    /// One account's export must not contain another's, on a server whose catalog is shared.
    /// </remarks>
    [Fact]
    public async Task An_export_holds_one_account_and_no_one_else()
    {
        using var owner = await RegisterOwnerAsync();
        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var theirs = await member.PostAsJsonAsync("/api/log", Payloads.AdHocLog(name: "Their toast"));
        theirs.EnsureSuccessStatusCode();

        using var response = await owner.GetAsync("/api/account/export");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Their toast", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_wants_the_password_and_the_word_typed()
    {
        using var client = await RegisterOwnerAsync();

        using var wrongPassword = await DeleteAsync(client, "not the password", "DELETE");
        Assert.Equal(HttpStatusCode.BadRequest, wrongPassword.StatusCode);

        using var noConfirmation = await DeleteAsync(client, OwnerPassword, "yes");
        Assert.Equal(HttpStatusCode.BadRequest, noConfirmation.StatusCode);

        // Still there, and still signed in.
        Assert.Equal(OwnerEmail, (await MeAsync(client)).Email);
    }

    /// <summary>
    /// Deleting takes the account's own data and leaves the server's.
    /// </summary>
    /// <remarks>
    /// The distinction the shared catalog forces: a personal item is this account's and goes, while
    /// a global one is the server's and other people's recipes are made of it.
    /// </remarks>
    [Fact]
    public async Task Deleting_takes_the_log_and_the_personal_catalog_but_not_the_shared_one()
    {
        using var owner = await RegisterOwnerAsync();
        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var personal = await CreateItemAsync(member, "Their own porridge", FoodVisibility.Personal);
        var global = await CreateItemAsync(member, "Household oats", FoodVisibility.Global);

        using var logged = await member.PostAsJsonAsync("/api/log", Payloads.AdHocLog(name: "Their toast"));
        logged.EnsureSuccessStatusCode();

        using var deleted = await DeleteAsync(member, OwnerPassword, "delete");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();

        Assert.Null(await db.Users.FirstOrDefaultAsync(user => user.Email == "member@example.test"));
        Assert.Null(await db.FoodItems.FirstOrDefaultAsync(item => item.Id == personal));
        Assert.NotNull(await db.FoodItems.FirstOrDefaultAsync(item => item.Id == global));
        Assert.Empty(await db.LogEntries.Where(entry => entry.Items.Any(i => i.Name == "Their toast")).ToListAsync());
    }

    /// <summary>
    /// The invite survives the person, and an unused one is revoked on the way out.
    /// </summary>
    /// <remarks>
    /// Two rules meeting. The record of who invited whom is worth keeping - it is why the foreign
    /// keys null rather than cascade - but an invite nobody redeemed is a live credential, and one
    /// of those must not outlive the account that issued it.
    /// </remarks>
    [Fact]
    public async Task An_account_that_minted_invites_can_still_be_deleted()
    {
        using var owner = await RegisterOwnerAsync();
        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var outstanding = await CreateInviteAsync(owner);

        using var deleted = await DeleteAsync(owner, OwnerPassword, "DELETE");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();

        var invites = await db.Invites.OrderBy(invite => invite.CreatedUtc).ToListAsync();

        Assert.Equal(2, invites.Count);
        Assert.All(invites, invite => Assert.Null(invite.CreatedByUserId));

        // The one the member used keeps its redemption record; the one nobody used is revoked.
        Assert.NotNull(invites[0].RedeemedUtc);
        Assert.Null(invites[1].RedeemedUtc);
        Assert.NotNull(invites[1].RevokedUtc);

        // And it can no longer be redeemed.
        using var stranger = Factory.NewClient();
        using var register = await stranger.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = "stranger@example.test",
            Password = OwnerPassword,
            InviteToken = outstanding
        });

        Assert.Equal(HttpStatusCode.BadRequest, register.StatusCode);
    }

    [Fact]
    public async Task Deleting_ends_the_session_it_was_asked_from()
    {
        using var client = await RegisterOwnerAsync();

        using var deleted = await DeleteAsync(client, OwnerPassword, "DELETE");
        deleted.EnsureSuccessStatusCode();

        using var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    private static Task<HttpResponseMessage> DeleteAsync(
        HttpClient client,
        string password,
        string confirmation) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/account")
        {
            Content = JsonContent.Create(new DeleteAccountRequest
            {
                Password = password,
                Confirmation = confirmation
            })
        });

    private static async Task<Guid> CreateItemAsync(HttpClient client, string name, FoodVisibility visibility)
    {
        using var response = await client.PostAsJsonAsync("/api/foods", new SaveFoodItemRequest
        {
            Name = name,
            ServingSize = 100m,
            ServingUnit = "g",
            Source = FoodSource.Manual,
            Visibility = visibility,
            EnergyKcal = 100m,
            FatG = 1m,
            CarbohydrateG = 10m,
            ProteinG = 5m
        });

        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<FoodItemResponse>())!.Id;
    }

    private static async Task<MeResponse> MeAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<MeResponse>("/api/auth/me"))!;
}
