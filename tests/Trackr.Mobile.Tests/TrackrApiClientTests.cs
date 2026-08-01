using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Platform;

namespace Trackr.Mobile.Tests;

/// <summary>
/// How the API client behaves when the network does not cooperate.
/// </summary>
/// <remarks>
/// Every one of these is a failure the user should be told about in words. The client sits
/// behind a Polly pipeline (via AddStandardResilienceHandler), so the exceptions it has to
/// survive are not only the ones a bare HttpClient raises - and an uncaught one here does not
/// surface as an error message, it terminates the app.
/// </remarks>
public sealed class TrackrApiClientTests
{
    private static readonly Uri Server = new("http://localhost:8000/");

    /// <summary>
    /// The regression this file was written for. A phone pointed at an unroutable address -
    /// "10.0.2.2" outside the emulator, say - neither connects nor gets refused, so the
    /// pipeline's total request timeout is what eventually fires, and it throws a Polly type
    /// rather than an HTTP one. Catching only HttpRequestException let it reach an async
    /// command with nothing above it and took the whole app down after ~30 seconds.
    /// </summary>
    [Fact]
    public async Task Reports_a_pipeline_timeout_instead_of_crashing()
    {
        var client = ClientThatThrows(new TimeoutRejectedException(TimeSpan.FromSeconds(30)));

        var result = await client.CheckServerAsync(Server);

        Assert.False(result.IsReachable);
        Assert.Contains("too long", result.Problem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The other exception the resilience pipeline can raise on its own.</summary>
    [Fact]
    public async Task Reports_an_open_circuit_instead_of_crashing()
    {
        var client = ClientThatThrows(new BrokenCircuitException());

        var result = await client.CheckServerAsync(Server);

        Assert.False(result.IsReachable);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
    }

    [Fact]
    public async Task Names_an_untrusted_certificate_specifically()
    {
        var client = ClientThatThrows(new HttpRequestException(
            "The SSL connection could not be established.",
            new System.Security.Authentication.AuthenticationException("untrusted root")));

        var result = await client.CheckServerAsync(Server);

        Assert.False(result.IsReachable);
        Assert.Contains("certificate", result.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reports_an_ordinary_connection_failure()
    {
        var client = ClientThatThrows(new HttpRequestException("Connection refused"));

        var result = await client.CheckServerAsync(Server);

        Assert.False(result.IsReachable);
        Assert.Contains("reach", result.Problem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Sign-in has to survive the same pipeline exceptions, and on the same reasoning: it runs
    /// from a command with no caller to catch anything it lets past.
    /// </summary>
    [Fact]
    public async Task Sign_in_survives_a_pipeline_timeout()
    {
        var client = ClientThatThrows(new TimeoutRejectedException(TimeSpan.FromSeconds(30)));

        var result = await client.SignInAsync(new Trackr.Shared.Auth.TokenRequest
        {
            Email = "owner@example.test",
            Password = "correct horse battery staple"
        });

        Assert.Equal(Trackr.Shared.Auth.LoginStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Problem));
    }

    [Fact]
    public async Task Token_refresh_survives_a_pipeline_timeout()
    {
        var client = ClientThatThrows(new TimeoutRejectedException(TimeSpan.FromSeconds(30)));

        // Null rather than an exception: the caller reads this as "sign in again".
        Assert.Null(await client.RefreshAsync("stale-refresh-token"));
    }

    [Fact]
    public async Task Identity_lookup_survives_a_pipeline_timeout()
    {
        var client = ClientThatThrows(new TimeoutRejectedException(TimeSpan.FromSeconds(30)));

        Assert.Null(await client.GetMeAsync());
    }

    /// <summary>A well-formed server that answers with an error still is not reachable.</summary>
    [Fact]
    public async Task Treats_a_non_success_status_as_unreachable()
    {
        var client = ClientThatResponds(HttpStatusCode.BadGateway);

        var result = await client.CheckServerAsync(Server);

        Assert.False(result.IsReachable);
        Assert.Contains("502", result.Problem);
    }

    [Fact]
    public async Task Accepts_a_server_that_answers()
    {
        var client = ClientThatResponds(HttpStatusCode.OK);

        Assert.True((await client.CheckServerAsync(Server)).IsReachable);
    }

    private static TrackrApiClient ClientThatThrows(Exception exception) =>
        Build(new StubHandler(_ => throw exception));

    private static TrackrApiClient ClientThatResponds(HttpStatusCode status) =>
        Build(new StubHandler(_ => new HttpResponseMessage(status)));


    private static TrackrApiClient Build(HttpMessageHandler handler)
    {
        var settings = Substitute.For<IServerSettings>();
        settings.BaseUrl.Returns(Server);

        return new TrackrApiClient(
            new HttpClient(handler),
            settings,
            NullLogger<TrackrApiClient>.Instance);
    }

    /// <summary>
    /// Stands in for the whole handler stack. The real one has the Polly pipeline in it; here
    /// the exceptions it would raise are thrown directly, which is what makes these tests run
    /// in milliseconds rather than waiting out a real timeout.
    /// </summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
