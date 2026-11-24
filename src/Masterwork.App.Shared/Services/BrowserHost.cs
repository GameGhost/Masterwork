using System.Text.Json;
using Masterwork.ModuleFormat;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// What every WebAssembly head registers and runs at startup: <c>Masterwork.App.Web.Client</c> (served
/// by the ASP.NET host) and <c>Masterwork.App.Web.Static</c> (served as static files). They differ only
/// in how they're hosted, so their services live here once.
/// </summary>
public static class BrowserHost
{
    /// <summary>
    /// Registers the browser implementations of the app's services. The theme is registered by the
    /// head itself (<see cref="ThemeModules.AddThemeModule"/>), since it's read from the head's own
    /// assembly.
    /// </summary>
    public static IServiceCollection AddBrowserHostServices(this IServiceCollection services)
    {
        // Not AddSingleton<IModuleLoader, ModuleLoader>(): DI can't resolve ModuleLoader's
        // parameterized constructor (IPassageYamlParser etc. aren't registered), so it would fall back
        // to the parameterless one and send ModuleLoader's own warnings (unresolved passage refs, stray
        // files) to a NullLogger instead of the browser console.
        services.AddSingleton<IModuleLoader>(sp => new ModuleLoader(
            new PassageYamlParser(), new VariableManifest(), new RestextFile(), new RestextResolver(),
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ModuleLoader>()));
        // Scoped, not Singleton: resolves against the current module's assets via GameSessionState (Scoped).
        services.AddScoped<IAssetResolver, AssetResolver>();
        services.AddScoped<IFormattedTextExpander, FormattedTextExpander>();
        services.AddScoped<IModuleStore, IndexedDbModuleStore>();
        services.AddScoped<IAssetPackStore, IndexedDbAssetPackStore>();
        services.AddScoped<IInstalledVoicePacks, NoInstalledVoicePacks>();
        services.AddScoped<NarrationService>();
        services.AddScoped<GameSessionState>();
        services.AddScoped<AppNavigationHistory>();
        services.AddScoped<ISaveStore, LocalStorageSaveStore>();
        services.AddScoped<IAppSettingsStore, LocalStorageAppSettingsStore>();
        services.AddScoped<IAudioPlayer, JsAudioPlayer>();
        services.AddScoped<AudioCoordinator>();
        services.AddScoped<AudioSettingsState>();
        services.AddScoped<IModuleStyleInjector, JsModuleStyleInjector>();

        // The primary source is always this origin's own content API; it never names an upstream host.
        // Where that API's content lives is the serving site's business. A source the player adds
        // later is fetched directly and must serve its own CORS.
        services.AddScoped(sp => ContentApi.ForSite(sp.GetRequiredService<NavigationManager>().BaseUri));
        services.AddHttpClient(HttpContentDownloader.HttpClientName);
        services.AddScoped<IContentDownloader, HttpContentDownloader>();
        services.AddScoped<BrowserCrypto>();
        services.AddScoped<SignatureVerifier>();
        services.AddScoped<CatalogService>();
        services.AddScoped<CatalogInstallService>();
        services.AddScoped<ThumbnailCache>();
        services.AddScoped<ModuleLibrary>();
        services.AddScoped<CatalogSubscriptions>();
        // The browser's InputFile elements don't hit the WebView2 crash INativeFilePicker exists for,
        // so this never-called default is enough.
        services.AddScoped<INativeFilePicker, NullNativeFilePicker>();

        return services;
    }

    /// <summary>
    /// Applies the player's saved UI language before the first render. Call after the host is built
    /// and before it runs: the JS interop channel is live by then, which allows a synchronous
    /// localStorage read, whereas MainLayout's own settings load comes after the first paint.
    /// </summary>
    public static void ApplyStartupCulture(IJSRuntime js)
    {
        try
        {
            var inProcess = (IJSInProcessRuntime)js;
            var json = inProcess.Invoke<string?>("localStorage.getItem", LocalStorageAppSettingsStore.Key);
            if (json is null)
            {
                return;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(nameof(AppSettings.UiLocale), out var uiLocale) && uiLocale.GetString() is { } locale)
            {
                AppSettingsApplier.ApplyCulture(locale);
            }
        }
        catch
        {
            // Best-effort: at worst the first paint is in English.
        }
    }
}
