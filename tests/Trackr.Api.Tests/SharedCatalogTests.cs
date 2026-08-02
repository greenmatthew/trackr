using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trackr.Api.Data;
using Trackr.Api.Identity;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The shared catalog: what a household can see, correct and delete.
/// </summary>
/// <remarks>
/// This amends CLAUDE.md section 7, which describes a food item as having an owning user. A
/// household sharing one server should scan a can of beans once between them, so
/// <c>FoodItem.UserId</c> is nullable and null means global.
/// </remarks>
public sealed class SharedCatalogTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    [Fact]
    public async Task A_personal_item_is_invisible_to_another_account()
    {
        using var owner = await RegisterOwnerAsync();
        var item = await FoodCatalogTests.CreateAsync(owner, Payloads.Food());

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var byId = await member.GetAsync($"/api/foods/{item.Id}");
        var listed = await member.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods");

        // 404 rather than 403, deliberately: a 403 would confirm that an item with that id exists.
        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);
        Assert.Empty(listed!);
    }

    [Fact]
    public async Task A_global_item_is_visible_to_every_account()
    {
        using var owner = await RegisterOwnerAsync();
        var item = await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(visibility: FoodVisibility.Global));

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var fetched = await member.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{item.Id}");

        Assert.Equal(FoodVisibility.Global, fetched!.Visibility);
        Assert.True(fetched.IsEditable);
    }

    /// <summary>The wiki-style edit, made executable.</summary>
    /// <remarks>
    /// The user's call, made knowing the trade: one person's mistake reaches everyone's future
    /// logs until somebody corrects it. Two things bound the damage - the edit is attributable,
    /// and no already-logged number moves (the next test).
    /// </remarks>
    [Fact]
    public async Task Another_account_may_correct_a_global_item()
    {
        using var owner = await RegisterOwnerAsync();
        var item = await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(visibility: FoodVisibility.Global, energyKcal: 210.5m));

        using var member = await RegisterMemberAsync(owner, "member@example.test");
        var memberId = await UserIdOfAsync("member@example.test");

        var correction = Payloads.Food(visibility: FoodVisibility.Global, energyKcal: 195m);
        using var response = await member.PutAsJsonAsync($"/api/foods/{item.Id}", correction);
        response.EnsureSuccessStatusCode();

        var asOwnerSeesIt = await owner.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{item.Id}");

        Assert.Equal(195m, asOwnerSeesIt!.EnergyKcal);
        Assert.Equal(memberId, asOwnerSeesIt.UpdatedByUserId);
    }

    /// <summary>The snapshot rule under the sharing rules - the most important test here.</summary>
    [Fact]
    public async Task Correcting_a_global_item_does_not_change_what_is_already_logged()
    {
        using var owner = await RegisterOwnerAsync();
        var item = await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(
                visibility: FoodVisibility.Global,
                energyKcal: 210.5m,
                nutrients: new Dictionary<string, decimal> { ["sodium"] = 120m }));

        using var logged = await owner.PostAsJsonAsync("/api/log", Payloads.LogOf(item));
        logged.EnsureSuccessStatusCode();

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var correction = await member.PutAsJsonAsync(
            $"/api/foods/{item.Id}",
            Payloads.Food(
                visibility: FoodVisibility.Global,
                energyKcal: 1m,
                nutrients: new Dictionary<string, decimal> { ["sodium"] = 1m }));
        correction.EnsureSuccessStatusCode();

        var entries = await owner.GetFromJsonAsync<LogEntryResponse[]>("/api/log");
        var loggedItem = Assert.Single(Assert.Single(entries!).Items);

        Assert.Equal(210.5m, loggedItem.EnergyKcal);
        Assert.Equal(120m, loggedItem.Nutrients["sodium"]);
    }

    [Fact]
    public async Task Sharing_is_one_way()
    {
        using var client = await RegisterOwnerAsync();
        var item = await FoodCatalogTests.CreateAsync(client, Payloads.Food());

        using var first = await client.PostAsync($"/api/foods/{item.Id}/share", content: null);
        using var second = await client.PostAsync($"/api/foods/{item.Id}/share", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var shared = await first.Content.ReadFromJsonAsync<FoodItemResponse>();
        Assert.Equal(FoodVisibility.Global, shared!.Visibility);
    }

    /// <summary>Proves the <c>WHERE "UserId" IS NULL</c> unique index.</summary>
    [Fact]
    public async Task Two_global_items_cannot_share_a_barcode()
    {
        using var client = await RegisterOwnerAsync();

        await FoodCatalogTests.CreateAsync(
            client,
            Payloads.Food(barcode: "5012345678900", visibility: FoodVisibility.Global));

        using var response = await client.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(name: "Second scan", barcode: "5012345678900", visibility: FoodVisibility.Global));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>And this proves the other one, <c>WHERE "UserId" IS NOT NULL</c>.</summary>
    [Fact]
    public async Task A_personal_override_may_reuse_a_global_barcode()
    {
        using var owner = await RegisterOwnerAsync();
        await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(barcode: "5012345678900", visibility: FoodVisibility.Global));

        using var response = await owner.PostAsJsonAsync(
            "/api/foods",
            Payloads.Food(name: "My own version", barcode: "5012345678900"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Sharing_is_refused_when_the_barcode_is_already_shared()
    {
        using var owner = await RegisterOwnerAsync();
        await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(barcode: "5012345678900", visibility: FoodVisibility.Global));

        var mine = await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(name: "My own version", barcode: "5012345678900"));

        using var response = await owner.PostAsync($"/api/foods/{mine.Id}/share", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <remarks>
    /// 403 here rather than the 404 an invisible item gets, and that is not a contradiction: a
    /// shared item's existence is not secret, so hiding it would only confuse. Removing one is
    /// left to a future admin surface.
    /// </remarks>
    [Fact]
    public async Task Nobody_may_delete_a_global_item()
    {
        using var owner = await RegisterOwnerAsync();
        var item = await FoodCatalogTests.CreateAsync(
            owner,
            Payloads.Food(visibility: FoodVisibility.Global));

        using var byOwner = await owner.DeleteAsync($"/api/foods/{item.Id}");

        using var member = await RegisterMemberAsync(owner, "member@example.test");
        using var byMember = await member.DeleteAsync($"/api/foods/{item.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, byOwner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, byMember.StatusCode);
    }

    /// <summary>
    /// The two different delete behaviours on one column - the part most likely to be got wrong.
    /// </summary>
    /// <remarks>
    /// Deleting the account goes through <see cref="UserManager{TUser}"/> rather than an endpoint,
    /// because account deletion is milestone 13. What is under test is the cascade, which exists
    /// now.
    /// <para>
    /// The invite rows are cleared first, and that is a finding rather than a convenience:
    /// <c>Invite</c>'s two foreign keys are <c>Restrict</c> on purpose (docs/decisions/02-auth.md -
    /// deleting an account must not erase who invited whom), so <strong>no invited account can be
    /// deleted while its invite exists</strong>. Milestone 13 will have to decide what happens to
    /// that record; this test only needs it out of the way to see the catalog cascade.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Deleting_an_account_leaves_its_global_items_standing_but_takes_its_personal_ones()
    {
        using var owner = await RegisterOwnerAsync();
        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var personal = await FoodCatalogTests.CreateAsync(member, Payloads.Food(name: "Members own"));
        var shared = await FoodCatalogTests.CreateAsync(
            member,
            Payloads.Food(name: "Household beans", visibility: FoodVisibility.Global));

        using (var scope = Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<TrackrUser>>();
            var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();

            var account = await users.FindByEmailAsync("member@example.test");

            await db.Invites
                .Where(invite => invite.RedeemedByUserId == account!.Id
                    || invite.CreatedByUserId == account!.Id)
                .ExecuteDeleteAsync();

            var result = await users.DeleteAsync(account!);

            Assert.True(result.Succeeded);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrackrDbContext>();

            Assert.False(await db.FoodItems.AnyAsync(item => item.Id == personal.Id));
            Assert.True(await db.FoodItems.AnyAsync(item => item.Id == shared.Id));
        }

        // And the survivor is still usable by everyone else, which is the entire point of it.
        var fetched = await owner.GetFromJsonAsync<FoodItemResponse>($"/api/foods/{shared.Id}");
        Assert.Equal("Household beans", fetched!.Name);
    }

    private async Task<Guid> UserIdOfAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<TrackrUser>>();

        var user = await users.FindByEmailAsync(email);

        return user!.Id;
    }
}
