using System.Net;
using System.Text;
using Microsoft.OpenApi;

namespace Trackr.Api.Tests.Docs;

/// <summary>
/// Renders an <see cref="OpenApiDocument"/> as the wiki's API reference page.
/// </summary>
/// <remarks>
/// Deliberately plain markdown with no HTML: the same file is published to both a GitHub and
/// a Gitea wiki, and the intersection of what those two render reliably is small.
/// <para>
/// Endpoints are grouped by the second path segment (<c>/api/auth/...</c> becomes "auth")
/// rather than by OpenAPI tag, because the endpoints carry no explicit tags and the default
/// tag is the assembly name, which would put all of them in one group called "Trackr.Api".
/// </para>
/// </remarks>
internal static class OpenApiMarkdown
{
    public static string Render(OpenApiDocument document)
    {
        var markdown = new StringBuilder();

        markdown.AppendLine("# API Reference");
        markdown.AppendLine();
        markdown.AppendLine(
            "<!-- Generated from the API's OpenAPI document by `just docs::api`. Do not edit: " +
            "your changes will be overwritten, and Trackr.Api.Tests fails when this page and " +
            "the code disagree. -->");
        markdown.AppendLine();
        markdown.AppendLine(
            "Every route Trackr serves, generated from the API itself. Routes are relative to " +
            "your server's address — `frontend` proxies everything under `/api/` to the backend.");
        markdown.AppendLine();
        markdown.AppendLine(
            "Two authentication schemes reach these endpoints: the website sends an HttpOnly " +
            "session cookie, and the Android app sends a bearer token. See " +
            "[Accounts and 2FA](Accounts-and-2FA).");
        markdown.AppendLine();
        markdown.AppendLine(
            "> **Response bodies are not listed.** The handlers return `IResult` and declare no " +
            "response type, so the OpenAPI document does not know their shapes and neither does " +
            "this page. The request bodies and schemas below are complete. For what a response " +
            "actually contains, read the DTOs in `Trackr.Shared` — they are the same types the " +
            "clients deserialise into.");
        markdown.AppendLine();

        foreach (var group in GroupPaths(document))
        {
            markdown.AppendLine($"## {group.Key}");
            markdown.AppendLine();

            foreach (var (path, item) in group.OrderBy(p => p.Path, StringComparer.Ordinal))
            {
                foreach (var (method, operation) in item.Operations ?? [])
                {
                    AppendOperation(markdown, method.Method.ToUpperInvariant(), path, operation);
                }
            }
        }

        AppendSchemas(markdown, document);

        return markdown.ToString();
    }

    private static IEnumerable<IGrouping<string, (string Path, IOpenApiPathItem Item)>> GroupPaths(
        OpenApiDocument document)
    {
        var paths = document.Paths ?? [];

        return paths
            .Select(entry => (Path: entry.Key, Item: entry.Value))
            .GroupBy(entry => GroupNameFor(entry.Path))
            .OrderBy(group => group.Key, StringComparer.Ordinal);
    }

    /// <summary>"/api/auth/token" groups under "auth"; anything shallower under its first segment.</summary>
    private static string GroupNameFor(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var name = segments.Length switch
        {
            0 => "root",
            1 => segments[0],
            _ when segments[0] == "api" => segments[1],
            _ => segments[0]
        };

        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static void AppendOperation(
        StringBuilder markdown,
        string method,
        string path,
        OpenApiOperation operation)
    {
        markdown.AppendLine($"### `{method} {path}`");
        markdown.AppendLine();

        if (!string.IsNullOrWhiteSpace(operation.Summary))
        {
            markdown.AppendLine(operation.Summary);
            markdown.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(operation.Description))
        {
            markdown.AppendLine(operation.Description);
            markdown.AppendLine();
        }

        AppendParameters(markdown, operation);
        AppendRequestBody(markdown, operation);
        AppendResponses(markdown, operation);
    }

    private static void AppendParameters(StringBuilder markdown, OpenApiOperation operation)
    {
        var parameters = operation.Parameters;

        if (parameters is null || parameters.Count == 0)
        {
            return;
        }

        markdown.AppendLine("| Parameter | In | Required | Type |");
        markdown.AppendLine("| --- | --- | --- | --- |");

        foreach (var parameter in parameters)
        {
            var required = parameter.Required ? "yes" : "no";

            markdown.AppendLine(
                $"| `{parameter.Name}` | {parameter.In?.ToString()?.ToLowerInvariant() ?? "—"} " +
                $"| {required} | {TypeNameOf(parameter.Schema)} |");
        }

        markdown.AppendLine();
    }

    private static void AppendRequestBody(StringBuilder markdown, OpenApiOperation operation)
    {
        var content = operation.RequestBody?.Content;

        if (content is null || content.Count == 0)
        {
            return;
        }

        var descriptions = content.Select(entry =>
            $"`{entry.Key}` → {TypeNameOf(entry.Value.Schema)}");

        markdown.AppendLine($"**Request body:** {string.Join(", ", descriptions)}");
        markdown.AppendLine();
    }

    /// <summary>
    /// Responses, as a table when the document says something useful about them and as a bare
    /// list of status codes when it does not.
    /// </summary>
    /// <remarks>
    /// Trackr's endpoints return <c>IResult</c> and declare no <c>Produces&lt;T&gt;()</c>, so
    /// today every operation carries exactly one undocumented 200 and a three-row table per
    /// endpoint would be noise. The table returns on its own if response types are ever
    /// declared — this is a rendering choice, not a decision to hide anything.
    /// </remarks>
    private static void AppendResponses(StringBuilder markdown, OpenApiOperation operation)
    {
        var responses = operation.Responses;

        if (responses is null || responses.Count == 0)
        {
            return;
        }

        var ordered = responses.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();

        var informative = ordered.Any(entry =>
            entry.Value.Content is { Count: > 0 } ||
            !IsBoilerplate(entry.Key, entry.Value.Description));

        if (!informative)
        {
            var codes = ordered.Select(entry => $"`{entry.Key}`");
            markdown.AppendLine($"**Responses:** {string.Join(", ", codes)}");
            markdown.AppendLine();
            return;
        }

        markdown.AppendLine("| Response | Body | Meaning |");
        markdown.AppendLine("| --- | --- | --- |");

        foreach (var (status, response) in ordered)
        {
            var body = response.Content is { Count: > 0 }
                ? string.Join(", ", response.Content.Select(c => TypeNameOf(c.Value.Schema)))
                : "—";

            var meaning = string.IsNullOrWhiteSpace(response.Description)
                ? "—"
                : response.Description.ReplaceLineEndings(" ");

            markdown.AppendLine($"| `{status}` | {body} | {meaning} |");
        }

        markdown.AppendLine();
    }

    /// <summary>True when a description says no more than the status code already does.</summary>
    private static bool IsBoilerplate(string status, string? description) =>
        string.IsNullOrWhiteSpace(description) ||
        description.Equals(ReasonPhraseFor(status), StringComparison.OrdinalIgnoreCase);

    /// <summary>"200" becomes "OK", so a description of "OK" can be recognised as saying nothing.</summary>
    private static string ReasonPhraseFor(string status) =>
        int.TryParse(status, out var code) && Enum.IsDefined((HttpStatusCode)code)
            ? ((HttpStatusCode)code).ToString()
            : status;

    private static void AppendSchemas(StringBuilder markdown, OpenApiDocument document)
    {
        var schemas = document.Components?.Schemas;

        if (schemas is null || schemas.Count == 0)
        {
            return;
        }

        markdown.AppendLine("## Schemas");
        markdown.AppendLine();
        markdown.AppendLine(
            "The request and response shapes above. These are the DTOs in `Trackr.Shared`, " +
            "which the web app and the Android app reference directly rather than generating " +
            "a client from this document.");
        markdown.AppendLine();

        foreach (var (name, schema) in schemas.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            markdown.AppendLine($"### `{name}`");
            markdown.AppendLine();

            var properties = schema.Properties;

            if (properties is null || properties.Count == 0)
            {
                markdown.AppendLine("No properties.");
                markdown.AppendLine();
                continue;
            }

            var required = schema.Required ?? new HashSet<string>(StringComparer.Ordinal);

            markdown.AppendLine("| Property | Type | Required |");
            markdown.AppendLine("| --- | --- | --- |");

            foreach (var (property, propertySchema) in properties)
            {
                var isRequired = required.Contains(property) ? "yes" : "no";
                markdown.AppendLine(
                    $"| `{property}` | {TypeNameOf(propertySchema)} | {isRequired} |");
            }

            markdown.AppendLine();
        }
    }

    /// <summary>
    /// A short, readable type for a schema: the component name when it is a reference,
    /// otherwise the JSON type, with arrays rendered as "array of X".
    /// </summary>
    private static string TypeNameOf(IOpenApiSchema? schema)
    {
        if (schema is null)
        {
            return "—";
        }

        if (schema is OpenApiSchemaReference reference)
        {
            return $"[`{reference.Reference.Id}`](#{Anchor(reference.Reference.Id)})";
        }

        if (schema.Items is not null)
        {
            return $"array of {TypeNameOf(schema.Items)}";
        }

        if (schema.Enum is { Count: > 0 })
        {
            var values = schema.Enum.Select(value => $"`{value}`");
            return string.Join(" \\| ", values);
        }

        return DescribeType(schema);
    }

    /// <summary>
    /// Renders <see cref="JsonSchemaType"/>, which is a flags enum because OpenAPI 3.1 types
    /// are a set.
    /// </summary>
    /// <remarks>
    /// Two cases are worth naming, since both look like bugs until you know why:
    /// <c>Null|String</c> is an ordinary <c>string?</c>, and <c>Integer|String</c> is an
    /// ordinary <c>int</c> — minimal APIs serialise with <c>JsonSerializerDefaults.Web</c>,
    /// whose <c>AllowReadingFromString</c> means a number really may arrive quoted.
    /// </remarks>
    private static string DescribeType(IOpenApiSchema schema)
    {
        if (schema.Type is not { } types)
        {
            return "object";
        }

        var nullable = types.HasFlag(JsonSchemaType.Null);

        var names = Enum.GetValues<JsonSchemaType>()
            .Where(flag => flag != JsonSchemaType.Null && types.HasFlag(flag))
            .Select(flag => flag.ToString().ToLowerInvariant())
            .ToList();

        var described = names.Count == 0 ? "object" : string.Join(" or ", names);

        if (!string.IsNullOrWhiteSpace(schema.Format))
        {
            described = $"{described} ({schema.Format})";
        }

        return nullable ? $"{described}, nullable" : described;
    }

    /// <summary>GitHub- and Gitea-style heading anchor for a schema name.</summary>
    private static string Anchor(string? name) =>
        name is null ? string.Empty : name.ToLowerInvariant();
}
