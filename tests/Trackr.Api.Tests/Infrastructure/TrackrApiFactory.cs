using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackr.Api.Data;
using Trackr.Api.Identity;

namespace Trackr.Api.Tests.Infrastructure;

/// <summary>
/// Boots the real application against the test container.
/// </summary>
public sealed class TrackrApiFactory(string connectionString, IDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development", which is what WebApplicationFactory would otherwise pick.
        // Development runs the #if DEBUG blocks in Program.cs (UseBlazorFrameworkFiles and
        // MapFallbackToFile), which expect the Blazor static web assets manifest to be
        // present at the test project's content root.
        builder.UseEnvironment("Testing");

        builder.UseSetting("ConnectionStrings:Trackr", connectionString);

        // The rate limiter partitions on the caller's IP, and TestServer has no remote
        // address - so every test in the assembly shares one partition. Left at the
        // production default, the limiter would start rejecting midway through the lockout
        // test and make it look flaky. Individual tests lower this on purpose.
        builder.UseSetting("Trackr:RateLimiting:LoginPermitLimit", "1000");
        builder.UseSetting("Trackr:RateLimiting:SensitivePermitLimit", "1000");

        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>
    /// A client with its own cookie jar, so each test gets an independent session.
    /// </summary>
    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        // https, not http. The "Testing" environment is not Development, so Program.cs
        // marks cookies Secure. CookieContainer will happily store a Secure cookie but
        // refuses to send it back over http, and every authenticated test would then fail
        // with a 401 that looks exactly like an auth bug. TestServer ignores the scheme.
        BaseAddress = new Uri("https://localhost")
    });

    /// <summary>Empties the tables each test writes to, leaving the schema and key ring alone.</summary>
    public async Task ResetDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();

        // Children first, though CASCADE would reach them anyway - the explicit list is what makes
        // a global food item (which has no owner to cascade from) go too.
        //
        // Not DataProtectionKeys: clearing the key ring mid-run would churn the keys that
        // encrypt cookies issued by other tests.
        //
        // Not Nutrients either, and for a different reason: that table is seeded once when the
        // factory boots the host, so truncating it would leave every test after the first looking
        // at an empty catalog and every nutrient key failing validation.
        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE "MealImages", "LogItemNutrients", "LogItems", "LogEntries", "FoodItemNutrients", "FoodItems", "Invites", "AspNetUserTokens", "AspNetUserLogins", "AspNetUserClaims", "AspNetUserRoles", "AspNetUsers" RESTART IDENTITY CASCADE;""");
    }

    /// <summary>
    /// Produces the authenticator code the user's phone would be showing, by reading their
    /// enrolled shared secret and computing it. Standing in for the phone - see <see cref="Totp"/>.
    /// </summary>
    public async Task<string> GenerateTotpAsync(string email)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TrackrUser>>();

        var user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user with email {email}.");

        var key = await users.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException($"{email} has not started 2FA enrolment.");

        return Totp.Generate(key);
    }

    /// <summary>Reads a password reset token directly, standing in for the emailed link.</summary>
    public async Task<string> GeneratePasswordResetCodeAsync(string email)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TrackrUser>>();

        var user = await users.FindByEmailAsync(email)
            ?? throw new InvalidOperationException($"No user with email {email}.");

        var token = await users.GeneratePasswordResetTokenAsync(user);

        return Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            System.Text.Encoding.UTF8.GetBytes(token));
    }
}
