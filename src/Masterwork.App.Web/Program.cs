using Masterwork.App.Shared.Services;
using Masterwork.App.Web.Components;
using Masterwork.ModuleFormat;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// Registered here too because AddInteractiveWebAssemblyRenderMode() prerenders components on the
// server before the WebAssembly client takes over — the same services Program.cs in
// Masterwork.App.Web.Client registers for the live WASM app are needed for that first render.
builder.Services.AddSingleton<IModuleLoader, ModuleLoader>();
builder.Services.AddSingleton<IAssetResolver, AssetResolver>();
builder.Services.AddScoped<IModuleStore, IndexedDbModuleStore>();
builder.Services.AddScoped<GameSessionState>();
builder.Services.AddScoped<ISaveStore, LocalStorageSaveStore>();
builder.Services.AddScoped<IAppSettingsStore, LocalStorageAppSettingsStore>();
builder.Services.AddScoped<IAudioPlayer, JsAudioPlayer>();

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
        typeof(Masterwork.App.Web.Client._Imports).Assembly);

app.Run();
