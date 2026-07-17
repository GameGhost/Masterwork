using Masterwork.App.Shared.Services;
using Masterwork.App.Web.Components;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Registered here too because AddInteractiveWebAssemblyRenderMode() prerenders components on the
// server before the WebAssembly client takes over — the same services Program.cs in
// Masterwork.App.Web.Client registers for the live WASM app are needed for that first render.
// Not AddSingleton<IModuleLoader, ModuleLoader>() — DI can't resolve ModuleLoader's parameterized
// constructor (IPassageYamlParser etc. aren't registered), so it silently falls back to the
// parameterless one, which discards all of ModuleLoader's own logging (unresolved passage refs,
// stray files that don't match "*.mws.yaml", ...) to a NullLogger. This factory gets those
// warnings into the same file/console log everything else uses instead.
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

// This only covers the server-side prerender pass and any server-side logging (e.g. the ASP.NET
// Core request pipeline itself) — the live WASM client that takes over afterward runs entirely in
// the browser sandbox and has no filesystem to log to; its errors surface via the browser console
// and the blazor-error-ui banner instead. See CLAUDE.md for the exact log location.
builder.Logging.AddMasterworkFileLogger(Path.Combine(builder.Environment.ContentRootPath, "logs"));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(
        typeof(Masterwork.App.Shared._Imports).Assembly,
        typeof(Masterwork.App.Web.Client._Imports).Assembly)
    .WithStaticAssets();

app.Run();
