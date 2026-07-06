using Masterwork.App.Services;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;

namespace Masterwork.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
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

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
