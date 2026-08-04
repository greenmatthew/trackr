namespace Trackr.Api.Cascade;

/// <summary>
/// Registers the cascade's stages.
/// </summary>
public static class CascadeStartupExtensions
{
    public static IServiceCollection AddTrackrCascade(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection(OpenFoodFactsOptions.SectionName);

        services.Configure<OpenFoodFactsOptions>(section);

        // Read once here as well, because a typed HttpClient's base address, timeout and User-Agent
        // are set when the client is built rather than per request.
        var options = section.Get<OpenFoodFactsOptions>() ?? new OpenFoodFactsOptions();

        services.AddHttpClient<IProductLookup, OpenFoodFactsClient>(client =>
        {
            client.BaseAddress = new Uri(options.BaseAddress);

            // No retry policy, and that is a decision rather than an omission - see
            // docs/decisions/08-barcode-off.md. The cascade's own fallback is the retry: a failed
            // lookup sends the photo to the model, which is a worse answer than a hit and a much
            // better one than making a free volunteer-run service absorb our retries.
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

            // Open Food Facts asks API callers to identify themselves, and throttles the ones that
            // do not (CLAUDE.md section 9, milestone 7).
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent());
        });

        // Singleton: the decoder holds no state between calls, and a new ZXing reader is built per
        // decode because its options are per-call state that must not be shared across requests.
        services.AddSingleton<IBarcodeDecoder, ZXingBarcodeDecoder>();

        return services;
    }
}
