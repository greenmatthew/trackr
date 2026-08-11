using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Trackr.Api.Cascade;

/// <summary>
/// Ollama's <c>POST /api/chat</c> request, as far as Trackr writes it.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from a client package. The surface used here is one endpoint and
/// a dozen fields, the project already has a typed-<c>HttpClient</c> pattern for exactly this
/// (<see cref="OpenFoodFactsClient"/>), and a dependency would have to be licence-checked against
/// CLAUDE.md section 10 for no saving worth the check.
/// </remarks>
public sealed record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OllamaMessage> Messages { get; init; }

    /// <summary>
    /// Always false. Streaming would let tokens arrive as they are generated, which milestone 9
    /// might want for a progress indicator and which also keeps a connection from looking idle to an
    /// intermediate proxy. Neither is needed to get an answer, and a single response is far simpler
    /// to validate as one document.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    /// <summary>
    /// Always false, and it has to be said rather than left out.
    /// </summary>
    /// <remarks>
    /// Some current models reason before answering and emit that reasoning first. Under a JSON
    /// grammar that is either wasted tokens against <c>num_predict</c> or a reply that is not the
    /// document it was asked for. Reading a nutrition label is not a task that benefits from it.
    /// </remarks>
    [JsonPropertyName("think")]
    public bool Think { get; init; }

    /// <summary>
    /// A JSON Schema the answer is constrained to, not the string <c>"json"</c>.
    /// </summary>
    /// <remarks>
    /// Ollama compiles this into a grammar that the sampler is restricted to, so the reply cannot be
    /// malformed JSON and cannot use a nutrient key the schema does not list. That is worth a great
    /// deal and is still not worth trusting - <c>MealAnalysisReader</c> validates the result anyway.
    /// See its remarks for what constrained decoding does and does not actually buy.
    /// </remarks>
    [JsonPropertyName("format")]
    public JsonNode? Format { get; init; }

    /// <summary>How long Ollama should keep the model resident afterwards. CLAUDE.md section 6.</summary>
    [JsonPropertyName("keep_alive")]
    public string? KeepAlive { get; init; }

    [JsonPropertyName("options")]
    public OllamaGenerationOptions? Options { get; init; }
}

/// <param name="Images">
/// Base64, with no <c>data:</c> prefix - Ollama wants the raw encoding. Null on any message that
/// carries no picture, and omitted from the wire rather than sent as null.
/// </param>
public sealed record OllamaMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    [JsonPropertyName("images")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Images { get; init; }
}

/// <summary>The sampler settings. All of them matter here, and two are not obvious.</summary>
public sealed record OllamaGenerationOptions
{
    /// <summary>Zero. This is a measurement task, not a writing one.</summary>
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    /// <summary>One, so sampling is greedy rather than merely cold.</summary>
    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 1;

    /// <summary>Fixed, so the same photograph gives the same answer twice in a row where it can.</summary>
    /// <remarks>
    /// Reproducible, not deterministic: llama.cpp results still shift with batch size and thread
    /// count, so two servers can disagree. It is enough to make a bad read investigable.
    /// </remarks>
    [JsonPropertyName("seed")]
    public int Seed { get; init; }

    /// <summary>
    /// One, meaning off - and this is the setting most likely to be "tidied" back to the default.
    /// </summary>
    /// <remarks>
    /// Ollama defaults it to 1.1, and unlike <c>top_k</c> and <c>top_p</c> it is not neutralised by
    /// a temperature of zero: it changes the logits before anything is picked. A nutrition table is
    /// full of legitimate repetition - the same field names on every entry, several zeroes in a row
    /// on a label - and penalising a repeated token there pushes the model off the correct answer
    /// towards a different one. Please leave this at 1.0.
    /// </remarks>
    [JsonPropertyName("repeat_penalty")]
    public double RepeatPenalty { get; init; } = 1.0;

    /// <summary>The context window. See <see cref="OllamaOptions.ContextLength"/> for why it is set.</summary>
    [JsonPropertyName("num_ctx")]
    public int NumCtx { get; init; }

    /// <summary>The output cap. See <see cref="OllamaOptions.MaxOutputTokens"/>.</summary>
    [JsonPropertyName("num_predict")]
    public int NumPredict { get; init; }
}

/// <summary>Ollama's reply to a non-streaming chat request.</summary>
/// <remarks>
/// The three counters at the end are read for logging only. They are what makes the "time it" step
/// in wiki/Ollama-Setup.md possible without adding instrumentation, and a <c>prompt_eval_count</c>
/// suspiciously close to the context length is the visible symptom of a prompt that was truncated.
/// </remarks>
public sealed record OllamaChatResponse
{
    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    /// <summary>
    /// Why generation stopped. <c>stop</c> is the good one; <c>length</c> means the answer was cut
    /// off at <c>num_predict</c> and is therefore truncated JSON, which the reader reports as its
    /// own kind of failure rather than as an unparseable reply.
    /// </summary>
    [JsonPropertyName("done_reason")]
    public string? DoneReason { get; init; }

    [JsonPropertyName("total_duration")]
    public long TotalDuration { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int PromptEvalCount { get; init; }

    [JsonPropertyName("eval_count")]
    public int EvalCount { get; init; }
}

/// <summary>The body Ollama returns with a failure status.</summary>
/// <remarks>
/// Worth reading rather than discarding, because one of its messages is a case the user can act on:
/// a model that has not been pulled yet comes back as a 404 whose text says the model was not found.
/// "The vision model is still downloading" and "the AI is unavailable" call for different reactions
/// from whoever reads them.
/// </remarks>
public sealed record OllamaErrorResponse
{
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
