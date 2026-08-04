using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Trackr.Api.Cascade;
using Trackr.Api.Tests.Infrastructure;
using Trackr.Shared.Nutrition;
using Xunit;
using ZXing;
using ZXing.Common;

namespace Trackr.Api.Tests;

/// <summary>
/// The lookup routes, through the real application.
/// </summary>
/// <remarks>
/// Open Food Facts is replaced by a stub in every test here - see
/// <see cref="OpenFoodFactsClientTests"/> for why the suite never calls the live service. The
/// barcode decoder is the real one, because it has no external dependency to stand in for.
/// <para>
/// The rule worth protecting in this file: <strong>a lookup writes nothing.</strong> It is the
/// obvious place for a well-meaning change to start filling the catalog automatically, which would
/// break confirm-before-save (CLAUDE.md section 2) and pre-empt milestone 10.
/// </para>
/// </remarks>
public sealed class BarcodeLookupTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    private const string Barcode = "3017620422003";

    [Fact]
    public async Task A_barcode_lookup_returns_what_the_database_said()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched());

        var result = await client.GetFromJsonAsync<ProductLookupResult>($"/api/lookup/barcode/{Barcode}");

        Assert.Equal(ProductLookupOutcome.Matched, result!.Outcome);
        Assert.Equal("Stub spread", result.Product!.Name);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("abcdefgh")]
    [InlineData("123456789012345")]
    public async Task Something_that_is_not_a_barcode_is_rejected(string barcode)
    {
        using var client = await SignedInClientAsync(StubLookup.Matched());

        using var response = await client.GetAsync($"/api/lookup/barcode/{barcode}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <remarks>
    /// The end-to-end path this milestone exists for: a photo goes in, a barcode comes out of it, and
    /// the number is looked up. Rendered rather than photographed, with the honest limits that
    /// implies - see <see cref="BarcodeDecoderTests"/>.
    /// </remarks>
    [Fact]
    public async Task A_photo_of_a_barcode_is_decoded_and_looked_up()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched());

        var imageId = await UploadAsync(client, RenderBarcode(Barcode));

        using var response = await client.PostAsync($"/api/lookup/image/{imageId}", content: null);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BarcodeScanResult>();

        Assert.Equal(Barcode, result!.Barcode);
        Assert.Equal(ProductLookupOutcome.Matched, result.Lookup.Outcome);
    }

    /// <remarks>
    /// Most meals are not packaged, so this is the common case rather than the sad path. It must be a
    /// 200 with a null barcode - the caller's signal to send the photo to the model - and not an
    /// error.
    /// </remarks>
    [Fact]
    public async Task A_photo_with_no_barcode_is_reported_without_a_lookup()
    {
        var stub = StubLookup.Matched();

        using var client = await SignedInClientAsync(stub);

        var imageId = await UploadAsync(client, RenderBlank());

        using var response = await client.PostAsync($"/api/lookup/image/{imageId}", content: null);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BarcodeScanResult>();

        Assert.Null(result!.Barcode);
        Assert.Equal(ProductLookupOutcome.NotFound, result.Lookup.Outcome);

        // No barcode means nothing to ask Open Food Facts about, and asking anyway would be a request
        // spent on a guess.
        Assert.Equal(0, stub.Calls);
    }

    /// <remarks>
    /// Meal photos are personal, unlike catalog items, so another account's photo is a 404 rather than
    /// a 403 - the same rule as <c>ImageEndpoints</c>, and for the same reason: a 403 would confirm
    /// that the id exists.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_photo_cannot_be_scanned()
    {
        using var owner = await SignedInClientAsync(StubLookup.Matched());

        var imageId = await UploadAsync(owner, RenderBarcode(Barcode));

        using var member = await RegisterMemberAsync(owner, "member@example.test");

        using var response = await member.PostAsync($"/api/lookup/image/{imageId}", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_photo_is_a_404()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched());

        using var response = await client.PostAsync(
            "/api/lookup/image/0198c0de-0000-7000-8000-000000000000", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <remarks>
    /// The whole point of separating <see cref="ProductLookupOutcome.Failed"/> from
    /// <see cref="ProductLookupOutcome.NotFound"/>: the user is told the number could not be checked,
    /// rather than being quietly shown an estimate as though it were a label.
    /// </remarks>
    [Fact]
    public async Task A_failed_lookup_reaches_the_caller_as_a_warning()
    {
        using var client = await SignedInClientAsync(
            StubLookup.Failing("Open Food Facts is rate-limiting requests."));

        var result = await client.GetFromJsonAsync<ProductLookupResult>($"/api/lookup/barcode/{Barcode}");

        Assert.Equal(ProductLookupOutcome.Failed, result!.Outcome);
        Assert.NotEmpty(result.Warnings);
    }

    /// <remarks>
    /// Confirm-before-save, asserted rather than assumed. A lookup is a question, and until the user
    /// confirms a card there is nothing to file - filling the catalog here is milestone 10's job and
    /// would have to be gated on a confirmation even then.
    /// </remarks>
    [Fact]
    public async Task A_lookup_never_writes_to_the_catalog()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched());

        using var lookup = await client.GetAsync($"/api/lookup/barcode/{Barcode}");
        lookup.EnsureSuccessStatusCode();

        var catalog = await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods");

        Assert.Empty(catalog!);
    }

    /// <summary>
    /// Boots the app with the stub standing in for Open Food Facts, and signs in.
    /// </summary>
    private async Task<HttpClient> SignedInClientAsync(StubLookup stub)
    {
        var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IProductLookup>(stub)));

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // https for the same reason NewClient uses it: the Testing environment marks cookies
            // Secure, and CookieContainer will not send those back over http.
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsJsonAsync(
            "/api/auth/register",
            new Trackr.Shared.Auth.RegisterRequest { Email = OwnerEmail, Password = OwnerPassword });

        response.EnsureSuccessStatusCode();

        return client;
    }

    private static async Task<Guid> UploadAsync(HttpClient client, byte[] image)
    {
        using var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        using var response = await client.PostAsync("/api/images", content);

        response.EnsureSuccessStatusCode();

        var uploaded = await response.Content.ReadFromJsonAsync<MealImageResponse>();

        return uploaded!.Id;
    }

    private static byte[] RenderBarcode(string barcode)
    {
        var writer = new ZXing.SkiaSharp.BarcodeWriter
        {
            Format = BarcodeFormat.EAN_13,
            Options = new EncodingOptions { Width = 600, Height = 300, Margin = 20 }
        };

        using var bitmap = writer.Write(barcode);

        return Encode(bitmap);
    }

    private static byte[] RenderBlank()
    {
        using var bitmap = new SKBitmap(320, 240);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
        }

        return Encode(bitmap);
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return data.ToArray();
    }

    /// <summary>Stands in for Open Food Facts, and counts whether it was asked anything.</summary>
    private sealed class StubLookup(ProductLookupResult result) : IProductLookup
    {
        public int Calls { get; private set; }

        public Task<ProductLookupResult> FindByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(result);
        }

        public static StubLookup Matched() =>
            new(ProductLookupResult.Matched(new ProductDraft(
                Barcode: Barcode,
                Name: "Stub spread",
                Brand: "Stub",
                ServingSize: 100m,
                ServingUnit: "g",
                ServingBasis: ServingBasis.ReferenceQuantityAsServing,
                EnergyKcal: 539m,
                FatG: 30.9m,
                CarbohydrateG: 57.5m,
                ProteinG: 6.3m,
                Nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["sugars"] = 56.3m
                })));

        public static StubLookup Failing(string reason) => new(ProductLookupResult.Failed(reason));
    }
}
