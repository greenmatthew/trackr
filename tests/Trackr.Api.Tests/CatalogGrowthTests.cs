using System.Net;
using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Milestone 10: what confirming a meal puts in the catalog, and - mostly - what it does not.
/// </summary>
/// <remarks>
/// The catalog is shared, wiki-editable and in one direction permanent, so an automatic writer into
/// it is a thing to keep on a short leash. Most of these tests assert an absence.
/// </remarks>
public sealed class CatalogGrowthTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    private const string Barcode = "0076840100446";

    [Fact]
    public async Task Confirming_a_barcode_item_files_it_and_links_the_log_row()
    {
        using var client = await RegisterOwnerAsync();

        using var logged = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: Barcode));
        logged.EnsureSuccessStatusCode();

        var item = await SingleCatalogItemAsync(client);

        Assert.Equal(Barcode, item.Barcode);
        Assert.Equal("Chocolate Therapy", item.Name);
        Assert.Equal(FoodSource.Off, item.Source);
        Assert.Equal(330m, item.EnergyKcal);

        var entry = await SingleEntryAsync(client);

        Assert.Equal(item.Id, Assert.Single(entry.Items).FoodItemId);
    }

    /// <remarks>
    /// The whole point of a key: eating the same product twice is one row, not two.
    /// </remarks>
    [Fact]
    public async Task Confirming_the_same_barcode_twice_files_one_item()
    {
        using var client = await RegisterOwnerAsync();

        using var first = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: Barcode));
        first.EnsureSuccessStatusCode();

        using var second = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: Barcode));
        second.EnsureSuccessStatusCode();

        var item = await SingleCatalogItemAsync(client);

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");

        Assert.Equal(2, entries!.Length);
        Assert.All(entries, entry => Assert.Equal(item.Id, Assert.Single(entry.Items).FoodItemId));
    }

    /// <summary>
    /// Confirming a meal consents to logging a meal, not to editing the catalog.
    /// </summary>
    /// <remarks>
    /// The corrections a card collects are often about the portion rather than the label, and
    /// writing those onto a stored product degrades it. Corrections belong to PUT /api/foods/{id},
    /// where they are deliberate and attributed.
    /// </remarks>
    [Fact]
    public async Task Confirming_never_changes_an_item_that_is_already_there()
    {
        using var client = await RegisterOwnerAsync();

        using var first = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: Barcode));
        first.EnsureSuccessStatusCode();

        var before = await SingleCatalogItemAsync(client);

        using var corrected = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.AdHocLog(barcode: Barcode, name: "Something else", energyKcal: 5m));
        corrected.EnsureSuccessStatusCode();

        var after = await SingleCatalogItemAsync(client);

        Assert.Equal(before.Id, after.Id);
        Assert.Equal("Chocolate Therapy", after.Name);
        Assert.Equal(330m, after.EnergyKcal);
        Assert.Equal(before.UpdatedUtc, after.UpdatedUtc);
    }

    /// <summary>
    /// Nothing filed here is global, because a global item is the one thing that cannot be undone.
    /// </summary>
    [Fact]
    public async Task Nothing_this_files_is_shared_with_the_household()
    {
        using var owner = await RegisterOwnerAsync();

        using var logged = await owner.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: Barcode));
        logged.EnsureSuccessStatusCode();

        var item = await SingleCatalogItemAsync(owner);

        Assert.Equal(FoodVisibility.Personal, item.Visibility);

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        Assert.Empty((await member.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods"))!);

        // And it stays deletable, which is what being personal buys.
        using var deleted = await owner.DeleteAsync($"/api/foods/{item.Id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    /// <remarks>
    /// CLAUDE.md section 7 forbids per-user duplicates of a shared product. A shared row is used
    /// where it exists rather than copied, which is where that rule bites.
    /// </remarks>
    [Fact]
    public async Task A_shared_item_with_that_barcode_is_used_rather_than_copied()
    {
        using var owner = await RegisterOwnerAsync();

        var shared = await FoodCatalogTests.CreateAsync(owner, Payloads.Food(barcode: Barcode));

        using var made = await owner.PostAsync($"/api/foods/{shared.Id}/share", content: null);
        made.EnsureSuccessStatusCode();

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var logged = await member.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: Barcode));
        logged.EnsureSuccessStatusCode();

        var visible = await member.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods");

        Assert.Equal(shared.Id, Assert.Single(visible!).Id);
        Assert.Equal(shared.Id, Assert.Single((await SingleEntryAsync(member)).Items).FoodItemId);
    }

    /// <remarks>
    /// A personal row for a barcode exists because its owner disagreed with the shared figures.
    /// Preferring the shared one would overrule them without saying so.
    /// </remarks>
    [Fact]
    public async Task A_personal_row_beats_the_shared_one()
    {
        using var owner = await RegisterOwnerAsync();

        var shared = await FoodCatalogTests.CreateAsync(owner, Payloads.Food(barcode: Barcode));

        using var made = await owner.PostAsync($"/api/foods/{shared.Id}/share", content: null);
        made.EnsureSuccessStatusCode();

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        var mine = await FoodCatalogTests.CreateAsync(
            member,
            Payloads.Food(name: "Mine", barcode: Barcode));

        using var logged = await member.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: Barcode));
        logged.EnsureSuccessStatusCode();

        Assert.Equal(mine.Id, Assert.Single((await SingleEntryAsync(member)).Items).FoodItemId);
    }

    /// <summary>
    /// The rule that keeps the catalog worth having: no barcode, no row.
    /// </summary>
    /// <remarks>
    /// A model's read of "two boiled eggs" has no key to find it by again, so a row for it buys
    /// nothing and costs a near-duplicate every meal.
    /// </remarks>
    [Fact]
    public async Task An_item_with_no_barcode_files_nothing()
    {
        using var client = await RegisterOwnerAsync();

        using var logged = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: null));
        logged.EnsureSuccessStatusCode();

        Assert.Empty((await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods"))!);
        Assert.Null(Assert.Single((await SingleEntryAsync(client)).Items).FoodItemId);
    }

    /// <remarks>
    /// FoodItem.ServingSize and ServingUnit are not nullable, and inventing "one serving" would
    /// record a measurement nobody took. The meal still saves.
    /// </remarks>
    [Fact]
    public async Task A_barcode_item_with_no_serving_files_nothing()
    {
        using var client = await RegisterOwnerAsync();

        using var logged = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.AdHocLog(barcode: Barcode, servingSize: null, servingUnit: null));
        logged.EnsureSuccessStatusCode();

        Assert.Empty((await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods"))!);
    }

    /// <summary>
    /// The row holds what the person approved, not what the model first said.
    /// </summary>
    [Fact]
    public async Task The_filed_item_holds_the_numbers_the_user_confirmed()
    {
        using var client = await RegisterOwnerAsync();

        using var logged = await client.PostAsJsonAsync(
            "/api/log",
            Payloads.AdHocLog(
                barcode: Barcode,
                quantity: 3m,
                energyKcal: 250m,
                nutrients: new Dictionary<string, decimal> { ["sodium"] = 65m }));
        logged.EnsureSuccessStatusCode();

        var item = await client.GetFromJsonAsync<FoodItemResponse>(
            $"/api/foods/{(await SingleCatalogItemAsync(client)).Id}");

        // Per serving on the catalog row; the quantity belongs to the log and is multiplied in there.
        Assert.Equal(250m, item!.EnergyKcal);
        Assert.Equal(65m, item.Nutrients["sodium"]);

        var logItem = Assert.Single((await SingleEntryAsync(client)).Items);

        Assert.Equal(750m, logItem.EnergyKcal);
    }

    /// <remarks>
    /// Editing history is milestone 14's business. A second silent writer into the catalog, with no
    /// user-facing trigger at all, is how a table fills with rows nobody chose.
    /// </remarks>
    [Fact]
    public async Task Replacing_an_entry_files_nothing()
    {
        using var client = await RegisterOwnerAsync();

        using var logged = await client.PostAsJsonAsync("/api/log", Payloads.AdHocLog(barcode: null));
        logged.EnsureSuccessStatusCode();

        var entry = await SingleEntryAsync(client);

        using var replaced = await client.PutAsJsonAsync(
            $"/api/log/{entry.Id}",
            Payloads.AdHocLog(barcode: Barcode));
        replaced.EnsureSuccessStatusCode();

        Assert.Empty((await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods"))!);
    }

    private static async Task<FoodItemSummaryResponse> SingleCatalogItemAsync(HttpClient client) =>
        Assert.Single((await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods"))!);

    private static async Task<LogEntryResponse> SingleEntryAsync(HttpClient client) =>
        Assert.Single((await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log"))!);
}
