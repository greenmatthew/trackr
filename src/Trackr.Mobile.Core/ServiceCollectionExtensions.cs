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

        // The system clock, injected rather than read statically so a view model that says "2 days
        // ago" can be tested without waiting two days.
        services.AddSingleton(TimeProvider.System);

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
        services.AddTransient<TrendsViewModel>();

        // Singleton, unlike the view models below: it holds the server's nutrient vocabulary, which
        // changes about once a year, and one fetch per visit to the chat would be a round trip for
        // an answer the process already had.
        services.AddSingleton<NutrientCatalogCache>();

        // Singleton, unlike the view models above, because the transcript is the screen: a chat that
        // empties itself on the way back from the Home tab reads as a crash rather than a design.
        // Milestone 14's offline queue is what gives a conversation somewhere to live that a phone
        // call cannot destroy; until then, surviving a tab switch is most of the value at none of
        // the cost.
        //
        // What it costs is the obligation AvatarStore already carries: state that outlives a visit
        // also outlives an account, so ChatViewModel takes AuthSession and empties itself on a
        // sign-out rather than leaving one person's meals for the next.
        services.AddSingleton<ChatViewModel>();

        services.AddTransient<ProfileViewModel>();
        services.AddTransient<AppShellViewModel>();

        return services;
    }
}
