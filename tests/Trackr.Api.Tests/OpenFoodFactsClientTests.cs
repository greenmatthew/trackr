using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trackr.Api.Cascade;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;
using Xunit;

namespace Trackr.Api.Tests;

/// <summary>
/// The Open Food Facts client's transport behaviour, against a stub rather than the real service.
/// </summary>
/// <remarks>
/// Deliberately never touches the network. Two reasons: a test suite that calls a volunteer-run free
/// API would be rude and flaky, and the cases worth testing here - rate limits, timeouts, truncated
/// responses - are precisely the ones a live service will not produce on demand. The live service is
/// exercised once, by hand, when the milestone is run for real.
/// <para>
/// Every case below asserts the same underlying rule from CLAUDE.md section 5: <strong>a stage that
/// fails says so and lets the cascade carry on.</strong> Nothing here may throw, and nothing may
/// quietly report "not found" for a failure, because those two answers lead the caller to do
/// different things.
/// </para>
/// </remarks>
public sealed class OpenFoodFactsClientTests
{
    private const string Barcode = "3017620422003";

    [Fact]
    public async Task A_found_product_is_mapped()
    {
        var (client, _) = Client(Respond(HttpStatusCode.OK, Fixture("nutella")));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.Matched, result.Outcome);
        Assert.Equal("Nutella", result.Product!.Name);
    }

    /// <remarks>
    /// The only thing that may leave this server is the barcode number (CLAUDE.md section 2). This
    /// test is the guard on that: the request carries the number and a field list, and nothing else -
    /// no account id, no meal, no photo, no text the user typed.
    /// </remarks>
    [Fact]
    public async Task A_lookup_sends_the_barcode_and_nothing_else()
    {
        var (client, handler) = Client(Respond(HttpStatusCode.OK, Fixture("nutella")));

        await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        var uri = handler.LastRequest!.RequestUri!;

        Assert.Equal($"/api/v2/product/{Barcode}.json", uri.AbsolutePath);
        Assert.Equal($"?fields={OpenFoodFactsNutrients.Fields}", Uri.UnescapeDataString(uri.Query));
        Assert.Equal(HttpMethod.Get, handler.LastRequest.Method);
        Assert.Null(handler.LastRequest.Content);
    }

    /// <remarks>
    /// How OFF reports an unknown barcode most of the time: HTTP 200, <c>status: 0</c>, no product.
    /// </remarks>
    [Fact]
    public async Task A_status_zero_response_is_not_found()
    {
        var (client, _) = Client(Respond(
            HttpStatusCode.OK,
            """{ "code": "0000000000000", "status": 0, "status_verbose": "product not found" }"""));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.NotFound, result.Outcome);
        Assert.Null(result.Product);

        // No warning: a product this database has never heard of is an ordinary answer, and the
        // cascade's next stage handles it. Warning about it would train the user to ignore warnings.
        Assert.Empty(result.Warnings);
    }

    /// <summary>The other way OFF says the same thing.</summary>
    [Fact]
    public async Task A_404_is_not_found()
    {
        var (client, _) = Client(Respond(HttpStatusCode.NotFound, "{}"));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.NotFound, result.Outcome);
    }

    /// <remarks>
    /// Named in CLAUDE.md section 5 as an example to surface rather than swallow, and distinguished
    /// from "not found" because the product may well exist - we simply were not told.
    /// </remarks>
    [Fact]
    public async Task A_rate_limit_is_a_failure_the_user_is_told_about()
    {
        var (client, _) = Client(Respond(HttpStatusCode.TooManyRequests, "rate limited"));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.Failed, result.Outcome);
        Assert.Contains(result.Warnings, warning => warning.Contains("rate-limiting"));
    }

    /// <remarks>
    /// A 429 is a request to stop, so it must not be retried - which is also why this client has no
    /// retry policy at all. One request in, one request out.
    /// </remarks>
    [Fact]
    public async Task A_rate_limit_is_not_retried()
    {
        var (client, handler) = Client(Respond(HttpStatusCode.TooManyRequests, "rate limited"));

        await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_server_error_is_a_failure_naming_the_status()
    {
        var (client, _) = Client(Respond(HttpStatusCode.ServiceUnavailable, "down"));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.Failed, result.Outcome);
        Assert.Contains(result.Warnings, warning => warning.Contains("503"));
    }

    /// <remarks>
    /// A truncated or HTML response - a proxy error page, say - must not become an exception out of a
    /// cascade stage that is allowed to fail.
    /// </remarks>
    [Fact]
    public async Task An_unreadable_response_is_a_failure_rather_than_an_exception()
    {
        var (client, _) = Client(Respond(HttpStatusCode.OK, "<html>not json at all"));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.Failed, result.Outcome);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public async Task An_unreachable_service_is_a_failure()
    {
        var (client, _) = Client((_, _) => throw new HttpRequestException("no route to host"));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.Failed, result.Outcome);
        Assert.Contains(result.Warnings, warning => warning.Contains("could not be reached"));
    }

    /// <remarks>
    /// A slow lookup is a user watching a chat message spin, and the cascade has a good fallback, so
    /// giving up quickly is correct behaviour rather than a compromise.
    /// </remarks>
    [Fact]
    public async Task A_timeout_is_a_failure()
    {
        var (client, _) = Client(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(100));

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.Failed, result.Outcome);
        Assert.Contains(result.Warnings, warning => warning.Contains("did not respond"));
    }

    /// <remarks>
    /// The one exception to "never throws". A caller that gave up - the user navigated away - has no
    /// card to show a warning on, and turning their cancellation into a reported failure would put a
    /// spurious warning in the logs of every abandoned request.
    /// </remarks>
    [Fact]
    public async Task A_cancelled_caller_is_not_reported_as_a_lookup_failure()
    {
        var (client, _) = Client(Respond(HttpStatusCode.OK, Fixture("nutella")));

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.FindByBarcodeAsync(Barcode, cancelled.Token));
    }

    /// <remarks>
    /// The privacy switch from <see cref="OpenFoodFactsOptions.Enabled"/>: a self-hoster who wants no
    /// outbound traffic at all gets exactly that, and the cascade falls through to the model.
    /// </remarks>
    [Fact]
    public async Task Lookups_can_be_turned_off_entirely()
    {
        var (client, handler) = Client(
            Respond(HttpStatusCode.OK, Fixture("nutella")),
            options: new OpenFoodFactsOptions { Enabled = false });

        var result = await client.FindByBarcodeAsync(Barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.NotFound, result.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    /// <remarks>
    /// Belt and braces on the value that gets interpolated into an outbound URL. Nothing should be
    /// able to reach this method with a non-barcode, so the assertion that matters is that no request
    /// is made.
    /// </remarks>
    [Theory]
    [InlineData("not-a-barcode")]
    [InlineData("12345")]
    [InlineData("123456789012345")]
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    public async Task Something_that_is_not_a_barcode_is_never_requested(string barcode)
    {
        var (client, handler) = Client(Respond(HttpStatusCode.OK, Fixture("nutella")));

        var result = await client.FindByBarcodeAsync(barcode, CancellationToken.None);

        Assert.Equal(ProductLookupOutcome.NotFound, result.Outcome);
        Assert.Equal(0, handler.Calls);
    }

    /// <remarks>
    /// Open Food Facts asks callers to identify themselves and throttles the ones that do not, which
    /// CLAUDE.md section 9 calls out for this milestone.
    /// </remarks>
    [Fact]
    public void The_user_agent_names_the_app_its_version_and_a_contact()
    {
        var agent = new OpenFoodFactsOptions { ContactEmail = "someone@example.test" }.UserAgent();

        Assert.StartsWith("Trackr/", agent);
        Assert.Contains("someone@example.test", agent);

        // Still identifies itself when no contact is configured, rather than falling back to
        // whatever HttpClient would have sent.
        Assert.StartsWith("Trackr/", new OpenFoodFactsOptions().UserAgent());
    }

    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Respond(
        HttpStatusCode status,
        string body) =>
        (_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });

    private static (OpenFoodFactsClient Client, StubHandler Handler) Client(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        OpenFoodFactsOptions? options = null,
        TimeSpan? timeout = null)
    {
        var handler = new StubHandler(respond);

        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://world.openfoodfacts.example/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10)
        };

        var client = new OpenFoodFactsClient(
            http,
            new NutrientCatalog(),
            Options.Create(options ?? new OpenFoodFactsOptions()),
            NullLogger<OpenFoodFactsClient>.Instance);

        return (client, handler);
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "OpenFoodFacts", $"{name}.json"));

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;

            return respond(request, cancellationToken);
        }
    }
}
