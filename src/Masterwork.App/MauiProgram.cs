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
        builder.Services.AddScoped<AudioCoordinator>();
        builder.Services.AddScoped<AudioSettingsState>();
        builder.Services.AddScoped<IModuleStyleInjector, JsModuleStyleInjector>();
        // Overrides Shared's NullNativeFilePicker default — see INativeFilePicker's own remarks for
        // why the MAUI head can't use the plain InputFile element Web/WASM use for module upload /
        // save import.
        builder.Services.AddScoped<INativeFilePicker, MauiNativeFilePicker>();
        // The active theme — swapping themes is a build-time decision (this line), not a runtime
        // one; see IMainMenuTheme's own remarks for why this wiring lives in each host rather than
        // the theme project registering itself.
        builder.Services.AddSingleton<IMainMenuTheme>(new MainMenuTheme(
            typeof(Masterwork.App.Theme.MyFathersWork.MainMenuScene),
            musicUrl: "_content/Masterwork.App.Theme.MyFathersWork/audio/main-menu-theme.ogg",
            transitionSfxUrl: "_content/Masterwork.App.Theme.MyFathersWork/audio/shell-transition.ogg",
            clickSfxUrl: "_content/Masterwork.App.Theme.MyFathersWork/audio/click.ogg"));

        // Always on, not just DEBUG — a file trail is what would have told us why the first upload
        // attempt crashed with no on-screen error (masterwork-plan-rev14.md). See CLAUDE.md for the
        // exact log location.
        //
        // Android specifically uses the app's external-files directory (Android/data/<package>/files),
        // not FileSystem.AppDataDirectory's private internal storage (Context.FilesDir) — the latter
        // needs `adb shell run-as` to reach at all, which a player reporting a bug can't be expected
        // to have; the external-files directory needs no special permission for the app's own files
        // (unlike shared/public storage) and is visible via USB/MTP from a PC. Falls back to
        // AppDataDirectory only if external storage genuinely isn't mounted (GetExternalFilesDir
        // returning null), which the Android docs allow for but is vanishingly rare on a real device.
#if ANDROID
        var logRoot = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
            ?? FileSystem.AppDataDirectory;
#else
        var logRoot = FileSystem.AppDataDirectory;
#endif
        builder.Logging.AddMasterworkFileLogger(Path.Combine(logRoot, "logs"));

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

                // See MainWindowState's own remarks — MauiModuleFilePicker needs this WindowId to
                // associate the native file picker with this window.
                Masterwork.App.Platforms.Windows.MainWindowState.Initialize(windowId);

                // Desktop has no true OS-level "backgrounded" concept the way mobile does — window
                // focus loss (alt-tab, clicking another window) is the closest real signal, so
                // that's what AppLifecycleAudioBridge pauses/resumes on here. Deliberately not tied
                // to minimize/restore specifically: WinUI doesn't expose a dedicated minimize event
                // on Window itself, and Activated already covers the common case. Revisit if pausing
                // on every focus loss (not just minimizing) turns out to feel too aggressive.
                window.Activated += (_, args) =>
                {
                    if (args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
                    {
                        AppLifecycleAudioBridge.OnBackgrounded();
                    }
                    else
                    {
                        AppLifecycleAudioBridge.OnForegrounded();
                    }
                };
            }));
        });
#endif

#if ANDROID
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddAndroid(android => android
                .OnPause(_ => AppLifecycleAudioBridge.OnBackgrounded())
                .OnResume(_ => AppLifecycleAudioBridge.OnForegrounded()));
        });
#endif

        var app = builder.Build();

        // Routes audio.js's own diagnostic trail into the same file log configured above — see
        // JsAudioLogBridge's own remarks for why this needs to be a static bridge rather than a
        // per-component DotNetObjectReference.
        JsAudioLogBridge.Configure(app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("audio.js"));

        return app;
    }
}
