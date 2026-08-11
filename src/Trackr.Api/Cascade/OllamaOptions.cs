namespace Trackr.Api.Cascade;

/// <summary>
/// How to talk to the local vision model. Documented for self-hosters in wiki/Configuration.md and
/// wiki/Ollama-Setup.md.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Trackr:Ollama";

    /// <summary>The Ollama server, which lives on the internal Docker network.</summary>
    /// <remarks>
    /// Configurable so a self-hoster can point at an Ollama running somewhere else - a machine with
    /// a GPU, most plausibly. It is still their own hardware either way: CLAUDE.md section 2's
    /// promise is that photos never reach a third party, and pointing this at a hosted API would
    /// break that promise silently, which is worth saying out loud next to the setting that could do
    /// it.
    /// </remarks>
    public string BaseAddress { get; set; } = "http://ollama:11434/";

    /// <summary>
    /// Which model to ask. Never hardcoded, per CLAUDE.md section 6 - swapping models is expected
    /// and should be an environment variable rather than a rebuild.
    /// </summary>
    /// <remarks>
    /// The default is chosen for a CPU-only server with plenty of RAM, which is the reference
    /// deployment. See wiki/Ollama-Setup.md for why a mixture-of-experts model beats a smaller dense
    /// one there, and for how to test a candidate before committing to it.
    /// </remarks>
    public string Model { get; set; } = "gemma4:26b";

    /// <summary>
    /// How long Ollama keeps the model in RAM after a request, in its own duration format.
    /// </summary>
    /// <remarks>
    /// CLAUDE.md section 6 wants this short so several gigabytes are not held permanently. Five
    /// minutes rather than zero, deliberately: a household logs a few times a day, so the model
    /// unloads between meals at anything under an hour, and the five minutes is what saves a full
    /// reload when somebody corrects a photo and immediately tries again.
    /// </remarks>
    public string KeepAlive { get; set; } = "5m";

    /// <summary>How long to wait for one analysis before giving up.</summary>
    /// <remarks>
    /// Long, unlike the Open Food Facts timeout, because there is no cheaper fallback behind this
    /// one - the model <em>is</em> the fallback. On a CPU-only server a vision model reading a label
    /// is tens of seconds to a couple of minutes.
    /// <para>
    /// It has to stay comfortably under the reverse proxy in front of it or the proxy answers first
    /// and the user gets a gateway error page instead of the plain-language explanation section 5
    /// requires. Trackr's own nginx allows 300 seconds; a self-hoster's proxy may well default to
    /// 60, which is a Troubleshooting entry rather than something this can control.
    /// </para>
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 240;

    /// <summary>
    /// The context window to ask for, in tokens.
    /// </summary>
    /// <remarks>
    /// <strong>Set explicitly, and that is not a tuning preference - it is the difference between
    /// working and silently not working.</strong> Ollama sizes the context from available VRAM, so a
    /// CPU-only host lands on the smallest tier, around 4096 tokens. When a prompt does not fit,
    /// Ollama <em>truncates it and does not report an error</em>: it drops the oldest tokens, which
    /// are the system prompt and the schema. The symptom is a model that appears to ignore every
    /// instruction, with nothing wrong anywhere in the logs.
    /// <para>
    /// <strong>A photograph is expensive in tokens and the number is not intuitive.</strong> One
    /// 1280-pixel image measured at roughly 7 000 tokens against a small vision model, so 8192 - the
    /// obvious first guess - fits the instructions and one picture and then fails on two. 16384 fits
    /// a realistic request; more photographs, or a larger
    /// <see cref="MaxImageEdgePixels"/>, need more again. Raising it costs RAM.
    /// </para>
    /// <para>
    /// Recent Ollama refuses an oversized prompt rather than truncating it, which is a great deal
    /// better - the analyzer turns that refusal into a sentence naming the two settings to change.
    /// </para>
    /// </remarks>
    public int ContextLength { get; set; } = 16384;

    /// <summary>
    /// The most tokens the model may generate before it is cut off.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose. Ollama's default is unlimited, and a model constrained to a JSON grammar
    /// can fall into repeating a valid string forever - the grammar cannot stop it, because repeated
    /// words are legal JSON. With a limit the answer comes back marked as truncated in bounded time,
    /// which the reader turns into a clear message, instead of holding a CPU core until the timeout.
    /// </remarks>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>How many photos one analysis may consider.</summary>
    /// <remarks>
    /// Each image costs context and, on a CPU, most of the wall-clock time. Four is more than a
    /// person photographs for one meal and few enough that the request stays inside
    /// <see cref="ContextLength"/>.
    /// </remarks>
    public int MaxImages { get; set; } = 4;

    /// <summary>
    /// The longest edge, in pixels, of the copy of a photo that is sent to the model.
    /// </summary>
    /// <remarks>
    /// A phone photograph is around 12 megapixels, which on its own can exceed the whole context
    /// window - see <see cref="ContextLength"/> - and which costs prefill time proportional to its
    /// size. 1280 keeps label print legible while bringing one image down to roughly a megapixel.
    /// <para>
    /// <strong>Only the copy is resized.</strong> The stored bytes are never re-encoded, which is
    /// the decision docs/decisions/08-barcode-off.md made and this does not reverse: the original is
    /// kept at full resolution precisely so a better model can be run over it later. The barcode
    /// decoder still reads the original too, because its hit rate was measured there.
    /// </para>
    /// </remarks>
    public int MaxImageEdgePixels { get; set; } = 1280;

    /// <summary>
    /// Whether to call the model at all. Off, every analysis reports a failure saying so.
    /// </summary>
    /// <remarks>
    /// Here for the same reason Open Food Facts has one: a self-hoster is entitled to decide in
    /// configuration that a part of the pipeline should not run, rather than having to break it. The
    /// difference is that turning this off leaves no fallback behind it - barcode lookups still
    /// work, and everything else stops - so the wiki says so plainly.
    /// </remarks>
    public bool Enabled { get; set; } = true;
}
