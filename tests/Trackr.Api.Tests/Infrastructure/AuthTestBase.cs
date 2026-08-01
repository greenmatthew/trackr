using System.Net.Http.Json;
using Trackr.Shared.Auth;
using Xunit;

namespace Trackr.Api.Tests.Infrastructure;

/// <summary>
/// Shared setup plus the handful of request helpers nearly every test needs.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class AuthTestBase : IAsyncLifetime
{
    protected const string OwnerEmail = "owner@example.test";
    protected const string OwnerPassword = "correct horse battery staple";

    private readonly PostgresFixture _postgres;
    private TrackrApiFactory? _factory;

    protected AuthTestBase(PostgresFixture postgres) => _postgres = postgres;

    protected TrackrApiFactory Factory => _factory!;

    public async Task InitializeAsync()
    {
        _factory = new TrackrApiFactory(_postgres.ConnectionString);

        // Force the host to start (and migrations to run) before truncating, otherwise the
        // very first test would truncate tables that do not exist yet.
        _ = _factory.Services;

        await _factory.ResetDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    /// <summary>Claims the first account and returns a client holding its session.</summary>
    protected async Task<HttpClient> RegisterOwnerAsync(
        string email = OwnerEmail,
        string password = OwnerPassword)
    {
        var client = Factory.NewClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest { Email = email, Password = password });

        response.EnsureSuccessStatusCode();

        return client;
    }

    protected static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password) =>
        client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest { Email = email, Password = password });

    protected static async Task<LoginResponse> LoginBodyAsync(
        HttpClient client,
        string email,
        string password)
    {
        using var response = await LoginAsync(client, email, password);

        return await response.Content.ReadFromJsonAsync<LoginResponse>()
            ?? throw new InvalidOperationException("The login endpoint returned no body.");
    }

    /// <summary>Mints an invite using an already signed-in client, returning the raw token.</summary>
    protected static async Task<string> CreateInviteAsync(HttpClient ownerClient)
    {
        using var response = await ownerClient.PostAsJsonAsync(
            "/api/invites",
            new CreateInviteRequest { Note = "test" });

        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<InviteCreatedResponse>();

        return created!.Token;
    }
}
