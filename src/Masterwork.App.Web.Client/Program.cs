using System.Text.Json;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Not AddSingleton<IModuleLoader, ModuleLoader>() — DI can't resolve ModuleLoader's parameterized
// constructor (IPassageYamlParser etc. aren't registered), so it silently falls back to the
// parameterless one, which discards all of ModuleLoader's own logging (unresolved passage refs,
// stray files that don't match "*.mws.yaml", ...) to a NullLogger. This factory routes those
// warnings to the browser console (via WebAssemblyHostBuilder's default logging) like everything
// else logged through DI-resolved loggers already does.
builder.Services.AddSingleton<IModuleLoader>(sp => new ModuleLoader(
    new PassageYamlParser(), new VariableManifest(), new RestextFile(), new RestextResolver(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ModuleLoader>()));
// Scoped, not Singleton: resolves against the current module's assets via GameSessionState (Scoped).
builder.Services.AddScoped<IAssetResolver, AssetResolver>();
builder.Services.AddScoped<IFormattedTextExpander, FormattedTextExpander>();
builder.Services.AddScoped<IModuleStore, IndexedDbModuleStore>();
builder.Services.AddScoped<GameSessionState>();
builder.Services.AddScoped<ISaveStore, LocalStorageSaveStore>();
builder.Services.AddScoped<IAppSettingsStore, LocalStorageAppSettingsStore>();
builder.Services.AddScoped<IAudioPlayer, JsAudioPlayer>();
builder.Services.AddScoped<IModuleStyleInjector, JsModuleStyleInjector>();

var host = builder.Build();

// Must run before RunAsync() starts the renderer — MainLayout's own settings-load-and-apply is
// already too late (the main view has already painted once by then). The JS interop channel is
// live as soon as the host is built (it doesn't depend on the renderer), which is what makes a
// *synchronous* localStorage read possible here, before the first render.
ApplyStartupCulture(host.Services.GetRequiredService<IJSRuntime>());

await host.RunAsync();

static void ApplyStartupCulture(IJSRuntime js)
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
        // Best-effort — worst case the main view paints once in English, same as before this fix.
    }
}
