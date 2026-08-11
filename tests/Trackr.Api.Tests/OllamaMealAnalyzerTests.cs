using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Trackr.Api.Cascade;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The analyzer as a caller of Ollama: what it sends, and how it explains a failure.
/// </summary>
/// <remarks>
/// Against a stub handler, never a real Ollama - a suite that needed a model container would be slow
/// enough that nobody ran it, and the failures worth testing here are the ones a working server will
/// not produce on demand.
/// <para>
/// The test worth protecting in this file is
/// <see cref="A_fully_matched_products_photograph_is_never_sent"/>. That is CLAUDE.md section 5's
/// central optimisation, and this is the layer where it is visible in bytes rather than in
/// intentions.
/// </para>
/// </remarks>
public sealed class OllamaMealAnalyzerTests
{
    private const string Empty = """{"note":"","items":[]}""";

    private const string OneItem =
        """{"note":"","items":[{"productRef":"none","name":"Toast","quantity":1,"energyKcal":80,"fatG":1,"carbohydrateG":15,"proteinG":3}]}""";

    [Fact]
    public async Task A_request_carries_the_model_the_schema_and_the_sampler_settings()
    {
        var (analyzer, handler) = Analyzer(_ => Ok(OneItem));

        await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        var body = handler.LastBody;

        Assert.Equal("test-model", body.GetProperty("model").GetString());
        Assert.False(body.GetProperty("stream").GetBoolean());

        // A model that reasons before answering would spend its output budget on prose the grammar
        // then has to be reconciled with. Reading a label does not benefit from it.
        Assert.False(body.GetProperty("think").GetBoolean());

        // The schema itself, not the string "json" - constrained decoding is what makes an invented
        // nutrient key impossible.
        Assert.Equal(JsonValueKind.Object, body.GetProperty("format").ValueKind);

        var options = body.GetProperty("options");

        Assert.Equal(0, options.GetProperty("temperature").GetDouble());
        Assert.Equal(1, options.GetProperty("top_k").GetInt32());

        // The one that is not neutralised by a temperature of zero, and the one most likely to be
        // "tidied" back to Ollama's 1.1 default. A nutrition table is full of legitimate repetition.
        Assert.Equal(1.0, options.GetProperty("repeat_penalty").GetDouble());

        // Set explicitly, because Ollama otherwise sizes the context from video memory and a
        // CPU-only host gets a window too small for the prompt - which it then truncates in silence.
        Assert.Equal(16384, options.GetProperty("num_ctx").GetInt32());
        Assert.True(options.GetProperty("num_predict").GetInt32() > 0);
    }

    /// <summary>
    /// CLAUDE.md section 5's token and accuracy win, asserted on the wire.
    /// </summary>
    /// <remarks>
    /// A fully matched barcode means Open Food Facts has already read that label properly. Sending
    /// the photograph anyway would spend the most expensive part of the request asking a small model
    /// to do the same job worse.
    /// </remarks>
    [Fact]
    public async Task A_fully_matched_products_photograph_is_never_sent()
    {
        var photo = Photo();
        var (analyzer, handler) = Analyzer(_ => Ok(OneItem));

        await analyzer.AnalyzeAsync(
            Request(photos: [photo], products: [Product(photo.Id, complete: true)]),
            CancellationToken.None);

        Assert.Null(Images(handler));
    }

    [Fact]
    public async Task A_partial_matchs_photograph_is_sent_alongside_what_was_found()
    {
        var photo = Photo();
        var (analyzer, handler) = Analyzer(_ => Ok(OneItem));

        await analyzer.AnalyzeAsync(
            Request(photos: [photo], products: [Product(photo.Id, complete: false)]),
            CancellationToken.None);

        Assert.Equal(1, Images(handler)!.Value.GetArrayLength());

        var system = handler.LastBody.GetProperty("messages")[0].GetProperty("content").GetString()!;

        Assert.Contains("Stub spread", system, StringComparison.Ordinal);
        Assert.Contains("One serving is 100 g.", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_photograph_that_matched_nothing_is_sent()
    {
        var (analyzer, handler) = Analyzer(_ => Ok(OneItem));

        await analyzer.AnalyzeAsync(
            Request(photos: [Photo()]), CancellationToken.None);

        Assert.Equal(1, Images(handler)!.Value.GetArrayLength());
    }

    /// <summary>
    /// The context window is finite and a phone photograph is not.
    /// </summary>
    /// <remarks>
    /// Without this the prompt does not fit, and Ollama's answer to a prompt that does not fit is to
    /// drop the oldest tokens - the instructions and the schema - without reporting anything at all.
    /// </remarks>
    [Fact]
    public async Task Photographs_are_scaled_down_before_they_are_sent()
    {
        var (analyzer, handler) = Analyzer(_ => Ok(OneItem));

        await analyzer.AnalyzeAsync(
            Request(photos: [Photo(2400, 1800)]), CancellationToken.None);

        var sent = Convert.FromBase64String(Images(handler)!.Value[0].GetString()!);

        using var codec = SKCodec.Create(SKData.CreateCopy(sent));

        Assert.NotNull(codec);
        Assert.True(
            Math.Max(codec.Info.Width, codec.Info.Height) <= 1280,
            $"A {codec.Info.Width}x{codec.Info.Height} image was sent to the model.");
    }

    /// <remarks>
    /// CLAUDE.md section 5 asks for earlier failures to be handed on, so the model can say "I
    /// couldn't reach the food database, so I estimated from your photo instead" rather than the
    /// user only seeing an error banner with no explanation attached to the answer.
    /// </remarks>
    [Fact]
    public async Task A_failed_earlier_stage_reaches_the_model_as_context()
    {
        var (analyzer, handler) = Analyzer(_ => Ok(OneItem));

        await analyzer.AnalyzeAsync(
            Request(problems: ["Open Food Facts is rate-limiting requests."]),
            CancellationToken.None);

        var system = handler.LastBody.GetProperty("messages")[0].GetProperty("content").GetString()!;

        Assert.Contains("rate-limiting", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ollama_being_unreachable_is_a_failure_the_user_is_told_about()
    {
        var (analyzer, _) = Analyzer(_ => throw new HttpRequestException("no route to host"));

        var reading = await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.False(reading.Succeeded);
        Assert.Contains("could not be reached", reading.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timeout_is_a_failure_naming_how_long_it_waited()
    {
        var (analyzer, _) = Analyzer(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Ok(OneItem);
            },
            timeout: TimeSpan.FromMilliseconds(100));

        var reading = await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.False(reading.Succeeded);
        Assert.Contains("did not answer within", reading.Failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first thing anybody hits on a fresh deployment.
    /// </summary>
    /// <remarks>
    /// Ollama does not download a model on demand, so until the pull finishes every request comes
    /// back as a 404. "Still downloading" and "unavailable" call for completely different reactions
    /// from whoever reads them, and only one of them is true here.
    /// </remarks>
    [Fact]
    public async Task A_model_that_has_not_been_pulled_yet_says_it_is_still_downloading()
    {
        var (analyzer, _) = Analyzer(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent(
                """{"error":"model 'test-model' not found"}""", Encoding.UTF8, "application/json")
        });

        var reading = await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.False(reading.Succeeded);
        Assert.Contains("still downloading", reading.Failure, StringComparison.Ordinal);
        Assert.Contains("test-model", reading.Failure, StringComparison.Ordinal);
    }

    /// <remarks>
    /// If the caller gave up there is nobody left to warn, so this is the one case that throws
    /// rather than reporting - the same contract, and the same reasoning, as
    /// <see cref="OpenFoodFactsClient"/>.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_caller_is_not_reported_as_a_failure()
    {
        var (analyzer, _) = Analyzer(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Ok(OneItem);
            });

        using var cancellation = new CancellationTokenSource();
        var analysis = analyzer.AnalyzeAsync(Request(), cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => analysis);
    }

    /// <summary>
    /// No retry, and that is a decision rather than an omission.
    /// </summary>
    /// <remarks>
    /// Retrying an inference that already cost a minute of processor time multiplies load on the one
    /// resource the whole feature is bottlenecked on, to repeat a request that is not idempotent and
    /// that failed for a reason a second attempt will not change.
    /// </remarks>
    [Fact]
    public async Task An_unusable_reply_is_not_retried()
    {
        var (analyzer, handler) = Analyzer(_ => Ok("not json at all"));

        var reading = await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.False(reading.Succeeded);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Analysis_can_be_turned_off_entirely()
    {
        var (analyzer, handler) = Analyzer(
            _ => Ok(OneItem), options: new OllamaOptions { Enabled = false });

        var reading = await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.False(reading.Succeeded);
        Assert.Contains("turned off", reading.Failure, StringComparison.Ordinal);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_model_that_identifies_nothing_is_a_failure_rather_than_an_empty_answer()
    {
        var (analyzer, _) = Analyzer(_ => Ok(Empty));

        var reading = await analyzer.AnalyzeAsync(Request(), CancellationToken.None);

        Assert.False(reading.Succeeded);
        Assert.Empty(reading.Items);
    }

    private static JsonElement? Images(StubHandler handler)
    {
        var user = handler.LastBody.GetProperty("messages")[1];

        return user.TryGetProperty("images", out var images) ? images : null;
    }

    private static (OllamaMealAnalyzer Analyzer, StubHandler Handler) Analyzer(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        OllamaOptions? options = null,
        TimeSpan? timeout = null) =>
        Analyzer((request, _) => Task.FromResult(respond(request)), options, timeout);

    private static (OllamaMealAnalyzer Analyzer, StubHandler Handler) Analyzer(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        OllamaOptions? options = null,
        TimeSpan? timeout = null)
    {
        var handler = new StubHandler(respond);

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://ollama.example/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10)
        };

        var analyzer = new OllamaMealAnalyzer(
            http,
            new NutrientCatalog(),
            Options.Create(options ?? new OllamaOptions { Model = "test-model" }),
            NullLogger<OllamaMealAnalyzer>.Instance);

        return (analyzer, handler);
    }

    private static HttpResponseMessage Ok(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                  {"model":"test-model","message":{"role":"assistant","content":{{JsonSerializer.Serialize(content)}}},"done":true,"done_reason":"stop","total_duration":1000000,"prompt_eval_count":10,"eval_count":20}
                  """,
                Encoding.UTF8,
                "application/json")
        };

    private static MealAnalysisRequest Request(
        string? text = "lunch",
        IReadOnlyList<KnownProduct>? products = null,
        IReadOnlyList<MealPhoto>? photos = null,
        IReadOnlyList<string>? problems = null) =>
        new(text, products ?? [], photos ?? [], problems ?? []);

    private static MealPhoto Photo(int width = 640, int height = 480)
    {
        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Beige);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);

        return new MealPhoto(Guid.CreateVersion7(), "image/png", data.ToArray());
    }

    private static KnownProduct Product(Guid imageId, bool complete) =>
        new("p1", imageId, complete, new ProductDraft(
            Barcode: "3017620422003",
            Name: "Stub spread",
            Brand: "Stub",
            ServingSize: 100m,
            ServingUnit: "g",
            ServingBasis: ServingBasis.ReferenceQuantityAsServing,
            EnergyKcal: 539m,
            FatG: 30.9m,
            CarbohydrateG: 57.5m,
            ProteinG: 6.3m,
            Nutrients: new Dictionary<string, decimal>(StringComparer.Ordinal) { ["sugars"] = 56.3m }));

    /// <summary>Stands in for Ollama, and keeps the request body so it can be asserted on.</summary>
    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public JsonElement LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;

            if (request.Content is not null)
            {
                LastBody = JsonDocument
                    .Parse(await request.Content.ReadAsStringAsync(cancellationToken))
                    .RootElement.Clone();
            }

            return await respond(request, cancellationToken);
        }
    }
}
