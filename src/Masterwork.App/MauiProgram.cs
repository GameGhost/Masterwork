using Masterwork.App.Services;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;

namespace Masterwork.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Must run before the BlazorWebView renders anything — MainLayout's own settings-load-and-
        // apply happens on first render, which is already too late (the main view has already
        // painted once by then). Preferences is synchronous, so this can run right here.
        PreferencesAppSettingsStore.ApplyStartupCulture();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddSingleton<IModuleLoader, ModuleLoader>();
        builder.Services.AddSingleton<IAssetResolver, AssetResolver>();
        builder.Services.AddScoped<IModuleStore, FileModuleStore>();
        builder.Services.AddScoped<GameSessionState>();
        builder.Services.AddScoped<ISaveStore, FileSaveStore>();
        builder.Services.AddScoped<IAppSettingsStore, PreferencesAppSettingsStore>();
        builder.Services.AddScoped<IAudioPlayer, JsAudioPlayer>();

        // Always on, not just DEBUG — a file trail is what would have told us why the first upload
        // attempt crashed with no on-screen error (masterwork-plan-rev14.md). See CLAUDE.md for the
        // exact log location.
        builder.Logging.AddMasterworkFileLogger(Path.Combine(FileSystem.AppDataDirectory, "logs"));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
