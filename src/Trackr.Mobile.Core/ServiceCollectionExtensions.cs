using Microsoft.Extensions.DependencyInjection;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
using Trackr.Mobile.Core.Nutrition;
using Trackr.Mobile.Core.Storage;
using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Core;

/// <summary>
/// Registers everything in this project.
/// </summary>
/// <remarks>
/// Kept here rather than in <c>MauiProgram</c> so the composition of Core can be exercised
/// by a test without booting MAUI. The caller supplies the platform implementations -
/// <see cref="Platform.ITokenStore"/>, <see cref="Platform.IServerSettings"/> and
/// <see cref="INavigationService"/> - because those are the parts that need Android.
/// </remarks>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTrackrCore(this IServiceCollection services)
    {
        // Singletons because the database holds one connection open for the process and
        // serialises callers through it - see LocalDatabase.
        services.AddSingleton<LocalDatabase>();
        services.AddSingleton<AccountCache>();

        services.AddSingleton<AuthSession>();

        // Singleton because two screens draw the same picture and must agree the moment it
        // changes - and because it subscribes to AuthSession.Changed to drop the bytes on
        // sign-out, which only works if there is exactly one of it.
        services.AddSingleton<AvatarStore>();

        services.AddTransient<BearerTokenHandler>();

        services.AddHttpClient<ITrackrApiClient, TrackrApiClient>(client =>
            {
                // No BaseAddress: the server is not known until first-run setup, and it can
                // change afterwards. TrackrApiClient builds absolute URIs instead.
                client.Timeout = TimeSpan.FromSeconds(30);
            })
            .AddHttpMessageHandler<BearerTokenHandler>()
            // Retry, timeout and circuit breaker. Worth more here than on the server: a
            // phone moves between wifi, cellular and VPN mid-request as a matter of course.
            .AddStandardResilienceHandler();

        // A second client for one route, because POST /api/analyze is not like the others.
        //
        // Analysing a meal runs a vision model on a CPU-only server and takes minutes, not
        // milliseconds. Under the pipeline above that is not slow, it is catastrophic: the
        // 30-second timeout fires, the resilience handler retries, and the phone queues four
        // inference jobs for one meal while showing the user a failure - with the server still
        // burning CPU on three requests nobody is waiting for. Long timeout, and deliberately no
        // resilience pipeline: a retry here has to be the user's decision, not the client's.
        //
        // BearerTokenHandler still applies. Its one retry is on a 401 only, which means the
        // request never reached the model at all.
        services.AddHttpClient(TrackrApiClient.AnalysisClientName, client =>
            {
                client.Timeout = TrackrApiClient.AnalysisTimeout;
            })
            .AddHttpMessageHandler<BearerTokenHandler>();

        // Transient, not singleton: a view model holds the state of one visit to a screen,
        // and a stale error message or a half-typed password surviving to the next visit is
        // the classic symptom of getting this wrong.
        services.AddTransient<ServerSetupViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<HomeViewModel>();

        // Singleton, unlike the view models below: it holds the server's nutrient vocabulary, which
        // changes about once a year, and one fetch per visit to the chat would be a round trip for
        // an answer the process already had.
        services.AddSingleton<NutrientCatalogCache>();

        // Transient like the rest, which means a transcript does not survive leaving the tab. That
        // is the right default until milestone 14's offline queue gives a conversation somewhere to
        // live - a half-finished chat kept only in memory would be lost to a phone call anyway.
        services.AddTransient<ChatViewModel>();

        services.AddTransient<ProfileViewModel>();
        services.AddTransient<AppShellViewModel>();

        return services;
    }
}
