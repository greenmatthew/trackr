using System.Net.Http.Json;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// Milestone 10's "I had that again", read out of the log rather than the catalog.
/// </summary>
public sealed class RecentItemsTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    private const string Barcode = "0076840100446";

    [Fact]
    public async Task The_same_food_logged_twice_is_offered_once_with_a_count()
    {
        using var client = await RegisterOwnerAsync();

        await LogAsync(client, Payloads.AdHocLog());
        await LogAsync(client, Payloads.AdHocLog());
        await LogAsync(client, Payloads.AdHocLog(name: "Toast", brand: null));

        var recent = await RecentAsync(client);

        Assert.Equal(2, recent.Length);
        Assert.Equal(2, recent.Single(item => item.Item.Name == "Chocolate Therapy").TimesLogged);
        Assert.Equal(1, recent.Single(item => item.Item.Name == "Toast").TimesLogged);
    }

    [Fact]
    public async Task Recent_items_are_the_callers_own_and_nobody_elses()
    {
        using var owner = await RegisterOwnerAsync();
        await LogAsync(owner, Payloads.AdHocLog());

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        Assert.Empty(await RecentAsync(member));
    }

    /// <summary>
    /// The arithmetic that makes a re-log a card rather than a portion.
    /// </summary>
    /// <remarks>
    /// A log item stores totals with the quantity multiplied in, and a save request wants per
    /// serving. Dividing back is what lets the card read "2 x 330 kcal" the second time as well.
    /// </remarks>
    [Fact]
    public async Task A_recent_item_reports_per_serving_values_the_log_stored_as_totals()
    {
        using var client = await RegisterOwnerAsync();

        await LogAsync(
            client,
            Payloads.AdHocLog(
                quantity: 3m,
                energyKcal: 250m,
                nutrients: new Dictionary<string, decimal> { ["sodium"] = 65m }));

        var item = Assert.Single(await RecentAsync(client)).Item;

        Assert.Equal(250m, item.EnergyKcal);
        Assert.Equal(65m, item.Nutrients["sodium"]);
        Assert.Equal(1m, item.Quantity);
    }

    /// <summary>Round-tripping a recent item reproduces what it was logged as.</summary>
    [Fact]
    public async Task Re_logging_a_recent_item_reproduces_its_totals()
    {
        using var client = await RegisterOwnerAsync();

        await LogAsync(client, Payloads.AdHocLog(quantity: 2m, energyKcal: 330m));

        var recent = Assert.Single(await RecentAsync(client)).Item;

        var again = new SaveLogEntryRequest
        {
            Items = [(recent with { Quantity = 2m }).ToSaveLogItemRequest()]
        };

        await LogAsync(client, again);

        var entries = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");

        Assert.All(entries!, entry => Assert.Equal(660m, Assert.Single(entry.Items).EnergyKcal));
    }

    /// <remarks>
    /// The barcode is what lets a re-logged packaged product file itself away exactly as the first
    /// one did. It is recovered through the catalog link, which is provenance rather than a number -
    /// the rule LogItem states is about not reading <em>values</em> back through it.
    /// </remarks>
    [Fact]
    public async Task A_recent_item_keeps_its_barcode_so_re_logging_still_files_it()
    {
        using var client = await RegisterOwnerAsync();

        await LogAsync(client, Payloads.AdHocLog(barcode: Barcode));

        var item = Assert.Single(await RecentAsync(client)).Item;

        Assert.Equal(Barcode, item.Barcode);
    }

    /// <summary>
    /// The photo stays with the meal it was taken for.
    /// </summary>
    /// <remarks>
    /// A photo already attached to another entry is refused, so carrying it forward would make
    /// every re-log of a photographed meal a 400.
    /// </remarks>
    [Fact]
    public async Task A_recent_item_never_carries_the_photo_of_the_meal_it_came_from()
    {
        using var client = await RegisterOwnerAsync();

        var image = await MealImageTests.UploadAsync(client);

        var request = Payloads.AdHocLog();
        request.ImageIds = [image.Id];

        await LogAsync(client, request);

        var item = Assert.Single(await RecentAsync(client)).Item;

        Assert.Null(item.MealImageId);
    }

    /// <remarks>
    /// Not the catalog's badge. These numbers are as good as whatever produced them the first time,
    /// which may well have been the model; what is true is that somebody confirmed them once.
    /// </remarks>
    [Fact]
    public async Task A_recent_item_says_it_came_from_the_log()
    {
        using var client = await RegisterOwnerAsync();

        await LogAsync(client, Payloads.AdHocLog());

        var item = Assert.Single(await RecentAsync(client)).Item;

        Assert.Equal(AnalyzedItemSource.PreviouslyLogged, item.Source);
        Assert.Equal(AnalysisConfidence.Normal, item.Confidence);
        Assert.Empty(item.Warnings);
    }

    private static async Task LogAsync(HttpClient client, SaveLogEntryRequest request)
    {
        using var response = await client.PostAsJsonAsync("/api/log", request);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<RecentItemResponse[]> RecentAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<RecentItemResponse[]>("/api/log/recent"))!;
}
