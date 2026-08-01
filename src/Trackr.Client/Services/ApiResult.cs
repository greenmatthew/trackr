using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Trackr.Client.Services;

/// <summary>
/// A failed API call, in the shape the forms need: one overall message plus any
/// per-field messages the server attached.
/// </summary>
/// <param name="Message">Human-readable summary, safe to show as-is.</param>
/// <param name="Errors">Field name to messages, from an RFC 9457 validation problem.</param>
/// <param name="StatusCode">The HTTP status, for the few places that branch on it.</param>
/// <param name="Code">
/// Machine-readable discriminator where the server sets one, e.g. <c>registration_closed</c>.
/// </param>
public sealed record ApiProblem(
    string Message,
    IReadOnlyDictionary<string, string[]> Errors,
    HttpStatusCode StatusCode,
    string? Code = null)
{
    /// <summary>Every message, field-level and overall, flattened for a summary block.</summary>
    public IEnumerable<string> AllMessages =>
        Errors.Count == 0 ? [Message] : Errors.SelectMany(entry => entry.Value);
}

public sealed record ApiResult<T>(T? Value, ApiProblem? Problem)
{
    public bool Succeeded => Problem is null;
}

public static class ApiResponse
{
    private const string GenericFailure = "Something went wrong. Please try again.";

    /// <summary>
    /// Reads a response body as <typeparamref name="T"/>, or turns a failure into an
    /// <see cref="ApiProblem"/>.
    /// </summary>
    /// <remarks>
    /// Errors must reach the user rather than vanishing (CLAUDE.md section 5), so an
    /// unparseable body still produces a message rather than a silent null.
    /// </remarks>
    public static async Task<ApiResult<T>> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            // 204 has no body; the caller is using T only to describe the success shape.
            if (response.StatusCode is HttpStatusCode.NoContent || response.Content.Headers.ContentLength is 0)
            {
                return new ApiResult<T>(default, null);
            }

            try
            {
                return new ApiResult<T>(await response.Content.ReadFromJsonAsync<T>(), null);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                return Failure<T>(new ApiProblem(GenericFailure, EmptyErrors, response.StatusCode));
            }
        }

        return Failure<T>(await ReadProblemAsync(response));
    }

    public static async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            var payload = await response.Content.ReadFromJsonAsync<ProblemPayload>();
            if (payload is not null)
            {
                var message = payload.Detail
                    ?? payload.Title
                    ?? DefaultMessageFor(response.StatusCode);

                return new ApiProblem(
                    message,
                    payload.Errors ?? EmptyErrors,
                    response.StatusCode,
                    payload.Code);
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Fall through to the status-based message.
        }

        return new ApiProblem(DefaultMessageFor(response.StatusCode), EmptyErrors, response.StatusCode);
    }

    private static ApiResult<T> Failure<T>(ApiProblem problem) => new(default, problem);

    private static IReadOnlyDictionary<string, string[]> EmptyErrors => new Dictionary<string, string[]>();

    private static string DefaultMessageFor(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Your session has ended. Please sign in again.",
        HttpStatusCode.Forbidden => "You do not have access to that.",
        HttpStatusCode.TooManyRequests => "Too many attempts. Please wait a moment and try again.",
        _ => GenericFailure
    };

    /// <summary>The subset of RFC 9457 problem+json this app actually reads.</summary>
    private sealed class ProblemPayload
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
        public string? Code { get; set; }
    }
}
