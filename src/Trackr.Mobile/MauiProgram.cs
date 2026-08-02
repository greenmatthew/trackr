using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Trackr.Mobile.Core;
using Trackr.Mobile.Core.Platform;
using Trackr.Mobile.Core.ViewModels;
using Trackr.Mobile.Pages;
using Trackr.Mobile.Platform;

namespace Trackr.Mobile;

/// <summary>
/// Composition root.
/// </summary>
/// <remarks>
/// <c>MauiApp.CreateBuilder()</c> is the same generic-host builder pattern as
/// <c>Microsoft.Extensions.Hosting</c>, which is why no DI, logging or configuration package
/// is referenced - <c>builder.Services</c>, <c>builder.Logging</c> and
/// <c>builder.Configuration</c> ship with MAUI. See CLAUDE.md section 3.
/// </remarks>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // Must be chained directly onto UseMauiApp - the toolkit ships an analyzer
            // (MCT001) that fails the build otherwise.
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // The Android implementations of the abstractions Trackr.Mobile.Core declares. Core
        // deliberately references no MAUI type, which is what keeps its view models testable
        // by plain `dotnet test`.
        builder.Services.AddSingleton<ITokenStore, SecureStorageTokenStore>();
        builder.Services.AddSingleton<IServerSettings, PreferencesServerSettings>();
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
        builder.Services.AddSingleton<ILocalStorePath, AppDataLocalStorePath>();
        builder.Services.AddSingleton<IPhotoPicker, MediaPickerPhotoPicker>();
        builder.Services.AddSingleton<IImageDownsizer, GraphicsImageDownsizer>();

        // View models, the API client and its handler pipeline.
        builder.Services.AddTrackrCore();

        // Pages are resolved from the container so Shell can constructor-inject their view
        // models. Without these registrations Shell falls back to Activator.CreateInstance
        // and fails on the parameterised constructors.
        builder.Services.AddTransient<ServerSetupPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<TrendsPage>();
        builder.Services.AddTransient<ProfilePage>();

        // Both shells too, for the same reason: App resolves one on each auth transition, and
        // AppShell constructor-injects the view model behind its title bar.
        builder.Services.AddTransient<AuthShell>();
        builder.Services.AddTransient<AppShell>();

        return builder.Build();
    }
}
