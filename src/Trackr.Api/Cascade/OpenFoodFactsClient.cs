using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Trackr.Api.Data;
using Trackr.Shared.Nutrition;

namespace Trackr.Api.Cascade;

/// <summary>
/// Looks products up in Open Food Facts' public API by barcode number.
/// </summary>
/// <remarks>
/// The public API rather than a self-hosted copy of the database (CLAUDE.md section 3): a lookup is
/// one small HTTP GET, against a dataset that is tens of gigabytes and changes daily.
/// <para>
/// <strong>A barcode number is the only thing that ever leaves the server.</strong> No image, no
/// account, no meal, no text the user typed - that is section 2's privacy line and this class is
/// where it is kept. Please do not add a parameter to this request that identifies anybody.
/// </para>
/// <para>
/// This class does transport and failure wording only; <see cref="OpenFoodFactsMapper"/> decides
/// what the numbers mean.
/// </para>
/// </remarks>
public sealed class OpenFoodFactsClient(
    HttpClient http,
    NutrientCatalog catalog,
    IOptions<OpenFoodFactsOptions> options,
    ILogger<OpenFoodFactsClient> logger) : IProductLookup
{
    /// <summary>
    /// The v2 product endpoint. <c>v2</c> rather than <c>v0</c> because it supports the
    /// <c>fields</c> parameter, which is the difference between a few hundred bytes and a hundred
    /// kilobytes per lookup.
    /// </summary>
    private const string ProductPath = "api/v2/product";

    private readonly OpenFoodFactsOptions _options = options.Value;

    public async Task<ProductLookupResult> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            logger.LogDebug("Open Food Facts lookups are disabled; reporting the product as unknown.");

            return ProductLookupResult.NotFound();
        }

        // Belt and braces: the decoder only ever produces digits and the endpoint validates, so a
        // failure here is a bug upstream rather than bad user input. Reported as "not found" because
        // there is genuinely nothing to look up, and interpolated into a URL only once it is known
        // to be digits.
        if (!IsPlausibleBarcode(barcode))
        {
            logger.LogWarning("Refused to look up {Barcode}, which is not a barcode.", barcode);

            return ProductLookupResult.NotFound();
        }

        var path = $"{ProductPath}/{barcode}.json?fields={OpenFoodFactsNutrients.Fields}";

        try
        {
            using var response = await http.GetAsync(path, cancellationToken);

            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                return ProductLookupResult.NotFound();
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests)
            {
                // Deliberately not retried, here or in the resilience policy: a 429 from a free
                // volunteer-run service is a request to stop, and the cascade has a perfectly good
                // fallback in the model.
                logger.LogWarning("Open Food Facts rate-limited a lookup.");

                return ProductLookupResult.Failed(
                    "Open Food Facts is rate-limiting requests, so this product could not be looked "
                        + "up.");
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Open Food Facts returned {StatusCode} for a lookup.", (int)response.StatusCode);

                return ProductLookupResult.Failed(
                    $"Open Food Facts returned an error (HTTP {(int)response.StatusCode}), so this "
                        + "product could not be looked up.");
            }

            var document = await response.Content.ReadFromJsonAsync<OpenFoodFactsResponse>(
                cancellationToken);

            // status 0 with a 200 is how OFF reports an unknown barcode most of the time; a 404 is
            // the other way. Both are the same answer.
            if (document is null || document.Status != 1 || document.Product is null)
            {
                return ProductLookupResult.NotFound();
            }

            var result = OpenFoodFactsMapper.Map(barcode, document.Product, catalog);

            logger.LogDebug(
                "Open Food Facts lookup for a barcode came back {Outcome}.", result.Outcome);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up - the user navigated away, the request was aborted. Not our failure
            // to report, so it propagates rather than becoming a warning on a card nobody will see.
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // Same exception type, different cause: our own timeout elapsed.
            logger.LogWarning(exception, "Open Food Facts lookup timed out.");

            return ProductLookupResult.Failed(
                $"Open Food Facts did not respond within {_options.TimeoutSeconds} seconds, so this "
                    + "product could not be looked up.");
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Open Food Facts was unreachable.");

            return ProductLookupResult.Failed(
                "Open Food Facts could not be reached, so this product could not be looked up.");
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Open Food Facts returned a response that could not be read.");

            return ProductLookupResult.Failed(
                "Open Food Facts returned a response this server could not read, so this product "
                    + "could not be looked up.");
        }
    }

    /// <summary>
    /// Digits, and a length a retail barcode actually has - EAN-8 through GTIN-14.
    /// </summary>
    public static bool IsPlausibleBarcode(string? barcode) =>
        barcode is { Length: >= 8 and <= 14 } && barcode.All(char.IsAsciiDigit);
}
