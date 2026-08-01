using Microsoft.Extensions.DependencyInjection;
using Trackr.Mobile.Core.Api;
using Trackr.Mobile.Core.Auth;
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
        services.AddSingleton<AuthSession>();

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

        // Transient, not singleton: a view model holds the state of one visit to a screen,
        // and a stale error message or a half-typed password surviving to the next visit is
        // the classic symptom of getting this wrong.
        services.AddTransient<ServerSetupViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<RegisterViewModel>();
        services.AddTransient<HomeViewModel>();

        return services;
    }
}
