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
/// <c>POST /api/analyze</c> through the real application, with the model and Open Food Facts stubbed.
/// </summary>
/// <remarks>
/// The barcode decoder is the real one, because it has no external dependency to stand in for - so
/// these tests also exercise the cascade's own decisions rather than only the route around them.
/// <para>
/// The rule worth protecting in this file: <strong>an analysis writes nothing.</strong> This is the
/// obvious place for a well-meaning change to start filling the catalog, which would break
/// confirm-before-save (CLAUDE.md section 2) and pre-empt milestone 10.
/// </para>
/// </remarks>
public sealed class MealAnalysisTests(PostgresFixture postgres) : AuthTestBase(postgres)
{
    private const string Barcode = "3017620422003";

    [Fact]
    public async Task An_analysis_writes_nothing()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched(), StubAnalyzer.Reading());

        using var response = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { Text = "two eggs" });

        response.EnsureSuccessStatusCode();

        var catalog = await client.GetFromJsonAsync<FoodItemSummaryResponse[]>("/api/foods");
        var log = await client.GetFromJsonAsync<LogEntryResponse[]>("/api/log");

        Assert.Empty(catalog!);
        Assert.Empty(log!);
    }

    [Fact]
    public async Task An_analysis_returns_what_the_model_read()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched(), StubAnalyzer.Reading());

        var result = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { Text = "two eggs" });

        var analysis = await result.Content.ReadFromJsonAsync<MealAnalysisResult>();

        Assert.Equal(MealAnalysisOutcome.Analyzed, analysis!.Outcome);

        var item = Assert.Single(analysis.Items);

        Assert.Equal("Toast", item.Name);
        Assert.Equal(AnalyzedItemSource.Model, item.Source);
    }

    [Fact]
    public async Task A_request_with_neither_words_nor_photos_is_rejected()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched(), StubAnalyzer.Reading());

        using var response = await client.PostAsJsonAsync("/api/analyze", new AnalyzeMealRequest());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <remarks>
    /// A cap rather than silently ignoring the extras: each photo is most of the wall-clock time of
    /// an analysis on a machine without a graphics card.
    /// </remarks>
    [Fact]
    public async Task More_photos_than_the_limit_are_rejected()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched(), StubAnalyzer.Reading());

        var ids = new List<Guid>();

        for (var index = 0; index < 5; index++)
        {
            ids.Add(await UploadAsync(client, RenderBlank()));
        }

        using var response = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { ImageIds = ids });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <remarks>
    /// A 404 rather than a 403, matching <c>ImageEndpoints</c>: a meal photo is personal, and whether
    /// one exists is not something to confirm to somebody else.
    /// </remarks>
    [Fact]
    public async Task A_photo_that_is_not_yours_cannot_be_analysed()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched(), StubAnalyzer.Reading());

        using var response = await client.PostAsJsonAsync(
            "/api/analyze",
            new AnalyzeMealRequest { Text = "lunch", ImageIds = [Guid.CreateVersion7()] });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Section 5's rule that a stage failure reaches the user whatever the model says.
    /// </summary>
    /// <remarks>
    /// The easy mistake is to build the warning list on the success path, so a model failure quietly
    /// takes the Open Food Facts warnings with it. Being told "the AI is unavailable" without also
    /// being told "and the food database was rate-limiting us" is materially less useful.
    /// </remarks>
    [Fact]
    public async Task A_lookup_failure_reaches_the_caller_even_when_the_model_also_fails()
    {
        using var client = await SignedInClientAsync(
            StubLookup.Failing("Open Food Facts is rate-limiting requests."),
            StubAnalyzer.Failing("The local model could not be reached."));

        var id = await UploadAsync(client, RenderBarcode(Barcode));

        var response = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { ImageIds = [id] });

        var analysis = await response.Content.ReadFromJsonAsync<MealAnalysisResult>();

        Assert.Equal(MealAnalysisOutcome.Failed, analysis!.Outcome);
        Assert.Contains(analysis.Warnings, warning => warning.Contains("rate-limiting", StringComparison.Ordinal));
        Assert.Contains(analysis.Warnings, warning => warning.Contains("could not be reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// A label reading is not a guess, so a model failure does not throw it away.
    /// </summary>
    /// <remarks>
    /// Section 5 forbids saving a guessed entry when the model fails - and this saves nothing. But
    /// the whole reason that photograph was withheld from the model is that the database's figures
    /// were already better, so discarding them because a different stage failed would contradict the
    /// cascade's own logic.
    /// </remarks>
    [Fact]
    public async Task A_matched_product_survives_the_model_failing()
    {
        using var client = await SignedInClientAsync(
            StubLookup.Matched(), StubAnalyzer.Failing("The local model could not be reached."));

        var id = await UploadAsync(client, RenderBarcode(Barcode));

        var response = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { ImageIds = [id] });

        var analysis = await response.Content.ReadFromJsonAsync<MealAnalysisResult>();

        Assert.Equal(MealAnalysisOutcome.Analyzed, analysis!.Outcome);

        var item = Assert.Single(analysis.Items);

        Assert.Equal("Stub spread", item.Name);
        Assert.Equal(539m, item.EnergyKcal);
        Assert.Equal(1m, item.Quantity);
        Assert.Equal(AnalyzedItemSource.Database, item.Source);
    }

    /// <summary>
    /// The model was not shown the photograph, so it may not connect the product to anything.
    /// </summary>
    /// <remarks>
    /// Dropping it would discard the best data in the cascade in favour of a guess made by a model
    /// that was deliberately never shown the picture.
    /// </remarks>
    [Fact]
    public async Task A_matched_product_the_model_never_mentioned_still_appears()
    {
        using var client = await SignedInClientAsync(StubLookup.Matched(), StubAnalyzer.Reading());

        var id = await UploadAsync(client, RenderBarcode(Barcode));

        var response = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { ImageIds = [id] });

        var analysis = await response.Content.ReadFromJsonAsync<MealAnalysisResult>();

        Assert.Contains(analysis!.Items, item => item.Source == AnalyzedItemSource.Database);
        Assert.Contains(
            analysis.Warnings,
            warning => warning.Contains("did not mention it", StringComparison.Ordinal));
    }

    /// <summary>
    /// The database's numbers beat the model's, which is the merge rule in one assertion.
    /// </summary>
    [Fact]
    public async Task A_matched_products_numbers_beat_the_models()
    {
        using var client = await SignedInClientAsync(
            StubLookup.Matched(), StubAnalyzer.Reading(productRef: "p1", energyKcal: 12));

        var id = await UploadAsync(client, RenderBarcode(Barcode));

        var response = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { ImageIds = [id] });

        var analysis = await response.Content.ReadFromJsonAsync<MealAnalysisResult>();

        var item = Assert.Single(analysis!.Items);

        Assert.Equal(539m, item.EnergyKcal);
        Assert.Equal(Barcode, item.Barcode);
        Assert.Equal(id, item.MealImageId);
    }

    /// <summary>
    /// A complaint about numbers that were then thrown away must not survive them.
    /// </summary>
    /// <remarks>
    /// Found by running the stack rather than by reading the code: the model described a drink as
    /// half a millilitre, the reader correctly warned that its figures weighed more than its serving,
    /// the database's real 473 ml serving then replaced it - and the warning stayed, sitting on a
    /// card whose numbers were entirely correct and describing figures that were no longer on it.
    /// </remarks>
    [Fact]
    public async Task A_matched_product_does_not_inherit_complaints_about_the_models_numbers()
    {
        using var client = await SignedInClientAsync(
            StubLookup.Matched(),
            StubAnalyzer.Reading(productRef: "p1", warning: "These figures weigh more than the serving."));

        var id = await UploadAsync(client, RenderBarcode(Barcode));

        var response = await client.PostAsJsonAsync(
            "/api/analyze", new AnalyzeMealRequest { ImageIds = [id] });

        var analysis = await response.Content.ReadFromJsonAsync<MealAnalysisResult>();

        var item = Assert.Single(analysis!.Items);

        Assert.Equal(AnalyzedItemSource.Database, item.Source);
        Assert.Empty(item.Warnings);
    }

    private async Task<HttpClient> SignedInClientAsync(StubLookup lookup, StubAnalyzer analyzer)
    {
        var factory = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IProductLookup>(lookup);
                services.AddSingleton<IMealAnalyzer>(analyzer);
            }));

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
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

    /// <summary>Stands in for Open Food Facts.</summary>
    private sealed class StubLookup(ProductLookupResult result) : IProductLookup
    {
        public Task<ProductLookupResult> FindByBarcodeAsync(
            string barcode,
            CancellationToken cancellationToken) => Task.FromResult(result);

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

    /// <summary>Stands in for the model, so no container is needed to exercise the route.</summary>
    private sealed class StubAnalyzer(ModelReading reading) : IMealAnalyzer
    {
        public Task<ModelReading> AnalyzeAsync(
            MealAnalysisRequest request,
            CancellationToken cancellationToken) => Task.FromResult(reading);

        public static StubAnalyzer Reading(
            string? productRef = null,
            decimal energyKcal = 80m,
            string? warning = null) =>
            new(ModelReading.Read([
                new ModelItem(
                    ProductReference: productRef,
                    Name: "Toast",
                    Brand: null,
                    Quantity: 1m,
                    ServingSize: null,
                    ServingUnit: null,
                    EnergyKcal: energyKcal,
                    FatG: 1m,
                    CarbohydrateG: 15m,
                    ProteinG: 3m,
                    Nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal),
                    Confidence: AnalysisConfidence.Normal,
                    Warnings: warning is null ? [] : [warning])
            ]));

        public static StubAnalyzer Failing(string reason) => new(ModelReading.Failed(reason));
    }
}
