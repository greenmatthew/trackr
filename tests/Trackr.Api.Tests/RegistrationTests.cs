using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// CLAUDE.md section 8.4: no open public sign-up. Exactly one account can be created
/// freely - the first - and everything after it needs a single-use invite.
/// </summary>
public sealed class RegistrationTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task First_account_registers_without_a_token_and_closes_registration()
    {
        using var client = Factory.NewClient();

        var before = await client.GetFromJsonAsync<RegistrationStatusResponse>("/api/auth/registration-status");
        Assert.Equal(RegistrationMode.Bootstrap, before!.Mode);

        using var registered = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest { Email = OwnerEmail, Password = OwnerPassword });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);

        // Registering signs you in, so the same client is now authenticated.
        using var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var after = await client.GetFromJsonAsync<RegistrationStatusResponse>("/api/auth/registration-status");
        Assert.Equal(RegistrationMode.InviteRequired, after!.Mode);
    }

    [Fact]
    public async Task Second_account_without_a_token_is_refused()
    {
        await RegisterOwnerAsync();

        using var stranger = Factory.NewClient();
        using var response = await stranger.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest { Email = "stranger@example.test", Password = "another long passphrase" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.Equal("registration_closed", body!["code"].ToString());
    }

    [Fact]
    public async Task Unknown_token_is_refused()
    {
        await RegisterOwnerAsync();

        using var stranger = Factory.NewClient();
        using var response = await stranger.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = "stranger@example.test",
                Password = "another long passphrase",
                InviteToken = "not-a-real-token"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Valid_token_works_exactly_once()
    {
        using var owner = await RegisterOwnerAsync();
        var token = await CreateInviteAsync(owner);

        using var first = Factory.NewClient();
        using var accepted = await first.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = "member@example.test",
                Password = "member long passphrase",
                InviteToken = token
            });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var second = Factory.NewClient();
        using var reused = await second.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = "gatecrasher@example.test",
                Password = "another long passphrase",
                InviteToken = token
            });
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
    }

    [Fact]
    public async Task Revoked_token_is_refused()
    {
        using var owner = await RegisterOwnerAsync();

        using var createResponse = await owner.PostAsJsonAsync(
            "/api/invites",
            new CreateInviteRequest { Note = "revoke me" });
        var created = await createResponse.Content.ReadFromJsonAsync<InviteCreatedResponse>();

        using var revoked = await owner.DeleteAsync($"/api/invites/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var stranger = Factory.NewClient();
        using var response = await stranger.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = "stranger@example.test",
                Password = "another long passphrase",
                InviteToken = created.Token
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Failed_registration_does_not_burn_the_token()
    {
        using var owner = await RegisterOwnerAsync();
        var token = await CreateInviteAsync(owner);

        // Too short for the 12-character minimum, so Identity rejects the user and the
        // surrounding transaction must roll back rather than leaving the invite redeemed.
        using var attempt = Factory.NewClient();
        using var rejected = await attempt.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest { Email = "member@example.test", Password = "short", InviteToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var retry = Factory.NewClient();
        using var accepted = await retry.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest
            {
                Email = "member@example.test",
                Password = "member long passphrase",
                InviteToken = token
            });

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }
}
