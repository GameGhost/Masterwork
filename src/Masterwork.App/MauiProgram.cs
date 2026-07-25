using Masterwork.App.Services;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.DependencyInjection;
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
        // Not AddSingleton<IModuleLoader, ModuleLoader>() — DI can't resolve ModuleLoader's
        // parameterized constructor (IPassageYamlParser etc. aren't registered), so it silently
        // falls back to the parameterless one, which discards all of ModuleLoader's own logging
        // (unresolved passage refs, stray files that don't match "*.mws.yaml", ...) to a
        // NullLogger. This factory gets those warnings into the same file/console log everything
        // else uses instead.
        builder.Services.AddSingleton<IModuleLoader>(sp => new ModuleLoader(
            new PassageYamlParser(), new VariableManifest(), new RestextFile(), new RestextResolver(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ModuleLoader>()));
        // Scoped, not Singleton: resolves against the current module's assets via GameSessionState (Scoped).
        builder.Services.AddScoped<IAssetResolver, AssetResolver>();
        builder.Services.AddScoped<IFormattedTextExpander, FormattedTextExpander>();
        builder.Services.AddScoped<IModuleStore, FileModuleStore>();
        builder.Services.AddScoped<GameSessionState>();
        builder.Services.AddScoped<ISaveStore, FileSaveStore>();
        builder.Services.AddScoped<IAppSettingsStore, PreferencesAppSettingsStore>();
        builder.Services.AddScoped<IAudioPlayer, JsAudioPlayer>();
        builder.Services.AddScoped<IModuleStyleInjector, JsModuleStyleInjector>();
        // Overrides Shared's NullModuleFilePicker default — see IModuleFilePicker's own remarks for
        // why the MAUI head can't use the plain InputFile element Web/WASM use for module upload.
        builder.Services.AddScoped<IModuleFilePicker, MauiModuleFilePicker>();

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
