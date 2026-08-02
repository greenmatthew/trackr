using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Trackr.Api.Tests.Infrastructure;
using Xunit;

namespace Trackr.Api.Tests.Docs;

/// <summary>
/// Keeps <c>wiki/API-Reference.md</c> in step with the API's own OpenAPI document.
/// </summary>
/// <remarks>
/// CLAUDE.md section 0: the page is generated and must never be hand-edited. This test both
/// generates it and enforces that - it writes the file when <c>TRACKR_UPDATE_DOCS=1</c> is
/// set (which is all <c>just docs::api</c> does), and otherwise asserts the committed page
/// matches, so an endpoint added without regenerating fails the build.
/// <para>
/// The document comes from <see cref="IOpenApiDocumentProvider"/> in the application's own
/// container rather than from <c>/openapi/v1.json</c>, because Program.cs maps that endpoint
/// only in Development and there is no reason to widen that for a test.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ApiReferenceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Page = "wiki/API-Reference.md";
    private const string UpdateVariable = "TRACKR_UPDATE_DOCS";

    private TrackrApiFactory? _factory;

    public Task InitializeAsync()
    {
        _factory = new TrackrApiFactory(postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Api_reference_matches_the_openapi_document()
    {
        var document = await GetDocumentAsync();
        var rendered = OpenApiMarkdown.Render(document);
        var path = RepositoryPath.Of(Page);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, rendered);
            return;
        }

        Assert.True(File.Exists(path), $"{Page} does not exist. Generate it with `just docs::api`.");

        var committed = await File.ReadAllTextAsync(path);

        Assert.True(
            Normalise(committed) == Normalise(rendered),
            $"{Page} no longer matches the API. Regenerate it with `just docs::api` - the page " +
            "is generated and must not be hand-edited.");
    }

    private async Task<OpenApiDocument> GetDocumentAsync()
    {
        var services = _factory!.Services;

        // AddOpenApi registers the provider keyed by document name; "v1" is the default that
        // Program.cs takes. Falling back to the unkeyed registration keeps this working if
        // that ever changes.
        var provider = services.GetKeyedService<IOpenApiDocumentProvider>("v1")
            ?? services.GetRequiredService<IOpenApiDocumentProvider>();

        return await provider.GetOpenApiDocumentAsync();
    }

    /// <summary>Line-ending and trailing-whitespace agnostic, so a checkout on Windows passes.</summary>
    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
}
