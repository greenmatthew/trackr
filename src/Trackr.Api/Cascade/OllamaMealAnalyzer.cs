using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Trackr.Api.Data;

namespace Trackr.Api.Cascade;

/// <summary>
/// Asks a local Ollama server to read a meal, and refuses to believe it without checking.
/// </summary>
/// <remarks>
/// <strong>The model runs on the user's own hardware and nothing here leaves it.</strong> That is
/// CLAUDE.md section 2's privacy line, and this class is where photographs would escape it if
/// anything ever pointed <see cref="OllamaOptions.BaseAddress"/> at a hosted API. Only Open Food
/// Facts sees anything at all, and only a barcode number.
/// <para>
/// Transport and failure wording only, in the same division of labour as
/// <see cref="OpenFoodFactsClient"/>: <see cref="MealPrompt"/> decides what to say and
/// <see cref="MealAnalysisReader"/> decides what the answer meant. This class knows how to make the
/// request and how to explain, in a sentence a person can act on, that it did not work.
/// </para>
/// </remarks>
public sealed class OllamaMealAnalyzer(
    HttpClient http,
    NutrientCatalog catalog,
    IOptions<OllamaOptions> options,
    ILogger<OllamaMealAnalyzer> logger) : IMealAnalyzer
{
    private const string ChatPath = "api/chat";

    private readonly OllamaOptions _options = options.Value;

    public async Task<ModelReading> AnalyzeAsync(
        MealAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogDebug("The local model is disabled; reporting the analysis as unavailable.");

            return ModelReading.Failed(
                "The local model is turned off on this server, so photos and descriptions cannot be "
                    + "read. Barcode lookups still work.");
        }

        var warnings = new List<string>();
        var images = Encode(request, warnings);

        var body = new OllamaChatRequest
        {
            Model = _options.Model,
            Messages = MealPrompt.Messages(catalog, request, images),
            Stream = false,
            Think = false,
            Format = MealPrompt.Schema(catalog, request),
            KeepAlive = _options.KeepAlive,
            Options = new OllamaGenerationOptions
            {
                NumCtx = _options.ContextLength,
                NumPredict = _options.MaxOutputTokens
            }
        };

        try
        {
            using var response = await http.PostAsJsonAsync(ChatPath, body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return await FailureAsync(response, warnings, cancellationToken);
            }

            var document = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken);

            if (document is null)
            {
                return ModelReading.Failed(
                    "The local model returned an empty response.", warnings: warnings);
            }

            // The three counters are the only instrumentation this milestone has, and they are what
            // wiki/Ollama-Setup.md's "time it" step reads. A prompt_eval_count sitting suspiciously
            // close to ContextLength is the visible symptom of a prompt that was silently truncated,
            // which is otherwise a very hard failure to see.
            logger.LogInformation(
                "The local model answered in {Milliseconds} ms, reading {PromptTokens} tokens and "
                    + "writing {ResponseTokens}.",
                document.TotalDuration / 1_000_000,
                document.PromptEvalCount,
                document.EvalCount);

            var references = request.KnownProducts
                .Select(product => product.Reference)
                .ToHashSet(StringComparer.Ordinal);

            var reading = MealAnalysisReader.Read(
                document.Message?.Content, document.DoneReason, catalog, references);

            if (warnings.Count == 0)
            {
                return reading;
            }

            return reading with { Warnings = [.. warnings, .. reading.Warnings] };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up - the chat was closed, the phone lost signal. Not our failure to
            // report, so it propagates rather than becoming a warning nobody will read.
            //
            // Worth knowing: with a non-streaming request this frees the thread waiting on the
            // answer, not the CPU producing it. Ollama keeps generating until it notices the closed
            // connection.
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // The same exception type, a different cause: our own timeout elapsed.
            logger.LogWarning(exception, "The local model did not answer in time.");

            return ModelReading.Failed(
                $"The local model did not answer within {_options.TimeoutSeconds} seconds. On a "
                    + "server without a graphics card this can simply mean the photo was too large; "
                    + "try again, or enter the values yourself.",
                warnings: warnings);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "The local model was unreachable.");

            return ModelReading.Failed(
                "The local model could not be reached, so this could not be read. Check that the "
                    + "ollama service is running.",
                warnings: warnings);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "The local model returned a response that could not be read.");

            return ModelReading.Failed(
                "The local model returned a response this server could not read.",
                warnings: warnings);
        }
    }

    /// <summary>
    /// Turns a failure status into something the reader can act on.
    /// </summary>
    /// <remarks>
    /// The 404 is the case worth separating, and it is the first thing anybody hits: Ollama does not
    /// download a model on demand, so until the pull finishes every request comes back saying the
    /// model was not found. "The vision model is still downloading" and "the AI is unavailable"
    /// prompt completely different reactions from whoever reads them, and only one of them is
    /// correct on a fresh deployment.
    /// </remarks>
    private async Task<ModelReading> FailureAsync(
        HttpResponseMessage response,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var error = await ReadErrorAsync(response, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            logger.LogWarning("The local model {Model} is not installed on the Ollama server.", _options.Model);

            return ModelReading.Failed(
                $"The vision model '{_options.Model}' is not on the AI server yet. If this server was "
                    + "only just set up, it is still downloading - try again in a few minutes.",
                warnings: warnings);
        }

        // The one other error worth naming, and the one this milestone actually hit while being
        // tested. A photograph costs thousands of tokens, so a couple of them plus the instructions
        // can be larger than the window the model was given - and "HTTP 400" tells whoever reads it
        // nothing about which of the two settings to change.
        if (error is not null && error.Contains("context size", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("The prompt did not fit the model's context window: {Error}", error);

            return ModelReading.Failed(
                "Your photos needed more room than the local model has been given. Send fewer photos, "
                    + "or raise TRACKR_OLLAMA_CONTEXT_LENGTH (or lower TRACKR_OLLAMA_MAX_IMAGE_EDGE) "
                    + "on the server.",
                warnings: warnings);
        }

        logger.LogWarning(
            "The local model returned {StatusCode}: {Error}", (int)response.StatusCode, error);

        return ModelReading.Failed(
            $"The local model returned an error (HTTP {(int)response.StatusCode}), so this could not "
                + "be read.",
            warnings: warnings);
    }

    private static async Task<string?> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            var document = await response.Content.ReadFromJsonAsync<OllamaErrorResponse>(cancellationToken);

            return document?.Error;
        }
        catch (Exception exception) when (exception is JsonException or HttpRequestException)
        {
            // An error body that is not JSON is not itself an error worth reporting - the status
            // code already said what happened.
            return null;
        }
    }

    /// <summary>
    /// Prepares the photographs that survive <see cref="MealPrompt.PhotosToSend"/>.
    /// </summary>
    /// <remarks>
    /// A photo that cannot be prepared is skipped with a warning rather than failing the request.
    /// The others may still be readable, and the user's text alone is often enough - section 5's
    /// rule is that a problem must be surfaced, not that it must be fatal.
    /// </remarks>
    private List<string> Encode(MealAnalysisRequest request, List<string> warnings)
    {
        var images = new List<string>();

        foreach (var photo in MealPrompt.PhotosToSend(request).Take(_options.MaxImages))
        {
            var encoded = MealImageDownscaler.ToBase64(
                photo.Content, photo.ContentType, _options.MaxImageEdgePixels, out var problem);

            if (encoded is null)
            {
                warnings.Add(problem ?? "One of your photos could not be prepared for the local model.");
                continue;
            }

            images.Add(encoded);
        }

        return images;
    }
}
