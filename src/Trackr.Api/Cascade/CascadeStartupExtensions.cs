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

        AddModel(services, configuration);

        // Scoped, because it is the per-request orchestration of the three stages above rather than
        // a stage of its own.
        services.AddScoped<MealCascade>();

        return services;
    }

    /// <summary>Stage three: the local vision model.</summary>
    private static void AddModel(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(OllamaOptions.SectionName);

        services.Configure<OllamaOptions>(section);

        var options = section.Get<OllamaOptions>() ?? new OllamaOptions();

        services.AddHttpClient<IMealAnalyzer, OllamaMealAnalyzer>(client =>
        {
            client.BaseAddress = new Uri(options.BaseAddress);

            // Set explicitly, and it matters: HttpClient's own default is 100 seconds, which is well
            // inside the range a CPU-only server legitimately takes to read a label. Left alone it
            // would cancel a request that was going to succeed, and report it as a timeout nobody
            // configured.
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        // No resilience handler, and unlike an omission this is a decision - see
        // docs/decisions/10-ollama.md. Retrying an inference that already cost a minute of CPU
        // multiplies the load on the one resource the whole feature is bottlenecked on, to repeat a
        // request that is not idempotent and that failed for a reason a second attempt will not
        // change. The user retrying is a person deciding it is worth the wait.
    }
}
