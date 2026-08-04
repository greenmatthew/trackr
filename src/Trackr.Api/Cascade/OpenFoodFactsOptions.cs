using System.Reflection;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// How to talk to Open Food Facts. Documented for self-hosters in wiki/Configuration.md.
/// </summary>
public sealed class OpenFoodFactsOptions
{
    public const string SectionName = "Trackr:OpenFoodFacts";

    /// <summary>
    /// The API root. Configurable so a self-hoster can point at a mirror, or at
    /// <c>world.openfoodfacts.net</c> - the staging server - while testing without adding noise to
    /// the real database's traffic.
    /// </summary>
    public string BaseAddress { get; set; } = "https://world.openfoodfacts.org/";

    /// <summary>
    /// A contact address to include in the User-Agent.
    /// </summary>
    /// <remarks>
    /// Optional but strongly encouraged, and the reason is social rather than technical: Open Food
    /// Facts is a volunteer-run free service, and their documented request is that API callers
    /// identify themselves so they can get in touch about a misbehaving client instead of simply
    /// blocking it. Unset, Trackr still sends its name and version.
    /// </remarks>
    public string? ContactEmail { get; set; }

    /// <summary>
    /// How long to wait for one attempt before giving up and letting the model estimate instead.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A user is watching a chat message spin, and section 5's fallback - send the
    /// photo to the model - is a perfectly good outcome. Waiting 30 seconds to avoid it would be the
    /// wrong trade.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Whether to look products up at all. Turning this off makes every lookup report
    /// <see cref="ProductLookupOutcome.NotFound"/>, so the cascade always falls through to the model.
    /// </summary>
    /// <remarks>
    /// Here because the barcode number leaving the server is the single exception to "nothing leaves
    /// this machine" (CLAUDE.md section 2). It is a very small exception - a number, no image, no
    /// account - but a self-hoster who wants literally no outbound traffic is entitled to say so,
    /// and should not have to block it at the firewall to get it.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The User-Agent to send, in the shape Open Food Facts asks for: name, version, contact.
    /// </summary>
    /// <remarks>
    /// A descriptive User-Agent is the one hard requirement in their API guidance - anonymous callers
    /// are the ones that get throttled, which section 9 notes for this milestone. This is built once
    /// at registration rather than per request.
    /// </remarks>
    public string UserAgent()
    {
        var version = typeof(OpenFoodFactsOptions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(OpenFoodFactsOptions).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";

        // The '+' suffix a build adds for source revision belongs in a build log, not in a header.
        var plus = version.IndexOf('+');

        if (plus > 0)
        {
            version = version[..plus];
        }

        var contact = ContactEmail?.Trim();

        return string.IsNullOrEmpty(contact)
            ? $"Trackr/{version} (self-hosted nutrition tracker)"
            : $"Trackr/{version} (self-hosted nutrition tracker; {contact})";
    }
}
