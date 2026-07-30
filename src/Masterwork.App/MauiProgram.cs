using Masterwork.App.Services;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;

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
        builder.Services.AddScoped<AppNavigationHistory>();
        builder.Services.AddScoped<ISaveStore, FileSaveStore>();
        builder.Services.AddScoped<IAppSettingsStore, PreferencesAppSettingsStore>();
        builder.Services.AddScoped<IAudioPlayer, JsAudioPlayer>();
        builder.Services.AddScoped<IModuleStyleInjector, JsModuleStyleInjector>();
        // Overrides Shared's NullModuleFilePicker default — see IModuleFilePicker's own remarks for
        // why the MAUI head can't use the plain InputFile element Web/WASM use for module upload.
        builder.Services.AddScoped<IModuleFilePicker, MauiModuleFilePicker>();
        // The active theme — swapping themes is a build-time decision (this line), not a runtime
        // one; see IMainMenuTheme's own remarks for why this wiring lives in each host rather than
        // the theme project registering itself.
        builder.Services.AddSingleton<IMainMenuTheme>(new MainMenuTheme(typeof(Masterwork.App.Theme.MyFathersWork.MainMenuScene)));

        // Always on, not just DEBUG — a file trail is what would have told us why the first upload
        // attempt crashed with no on-screen error (masterwork-plan-rev14.md). See CLAUDE.md for the
        // exact log location.
        builder.Logging.AddMasterworkFileLogger(Path.Combine(FileSystem.AppDataDirectory, "logs"));

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

#if WINDOWS
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windows => windows.OnWindowCreated(window =>
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

                // WinUI3's own title bar (and taskbar/Alt-Tab preview) reads from AppWindow.SetIcon,
                // not from the exe's embedded Win32 icon resource that MauiIcon/Resizetizer already
                // wires via ApplicationIcon — the two are set independently, so the title bar needs
                // this separate call even though the taskbar/File-Explorer icon already works
                // without it. appicon.ico is the same file Resizetizer generates from MauiIcon and
                // copies next to the exe.
                var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
                if (File.Exists(iconPath))
                {
                    appWindow.SetIcon(iconPath);
                }

                // F11 fullscreen toggle — see FullscreenToggle's own remarks for why this hands off
                // to a JS keydown listener (wwwroot/fullscreenToggle.js) instead of a native WinUI
                // KeyboardAccelerator (tried first; didn't fire with focus inside the BlazorWebView,
                // and left a stuck "F11" hint tooltip on screen).
                Masterwork.App.Platforms.Windows.FullscreenToggle.Initialize(appWindow);
            }));
        });
#endif

        return builder.Build();
    }
}
