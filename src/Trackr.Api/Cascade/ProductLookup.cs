using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Stage two of the cascade: turn a barcode number into nutrition data.
/// </summary>
/// <remarks>
/// Behind an interface because CLAUDE.md section 2 requires each cascade stage to be swappable - a
/// different nutrition database, or a cached copy, should not touch anything downstream.
/// <para>
/// <strong>Implementations must not throw.</strong> Every failure is a
/// <see cref="ProductLookupOutcome.Failed"/> result carrying a human-readable reason, because
/// section 5 requires the reason to travel: into the model's prompt so it can explain itself, and
/// onto the confirmation card so the user knows a fallback happened. An exception escaping here
/// would abort a whole log attempt over a stage that is allowed to fail.
/// </para>
/// <para>
/// The one exception is cancellation. If the <em>caller</em> gave up, that is not a lookup failure to
/// report to anybody, so an <see cref="OperationCanceledException"/> from the caller's own token
/// propagates.
/// </para>
/// <para>
/// The result types live in <c>Trackr.Shared</c> rather than here: a lookup result is something the
/// Android app renders, so it is a contract rather than an implementation detail.
/// </para>
/// </remarks>
public interface IProductLookup
{
    /// <param name="barcode">Digits only, as <see cref="IBarcodeDecoder"/> reports them.</param>
    Task<ProductLookupResult> FindByBarcodeAsync(string barcode, CancellationToken cancellationToken);
}
