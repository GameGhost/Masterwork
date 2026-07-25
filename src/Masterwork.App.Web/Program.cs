using Masterwork.App.Shared.Services;
using Masterwork.App.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// No IModuleLoader/IModuleStore/GameSessionState/etc. registrations here — those back the actual
// game UI, which only ever runs client-side. Server-side prerendering is deliberately off
// (App.razor's own `prerender: false`, see its comment for why), so this host never renders any of
// that component tree itself; registering game-domain services here would be dead code.
//
// This only covers server-side logging (e.g. the ASP.NET Core request pipeline itself) — the live
// WASM client that takes over after Blazor.start() runs entirely in the browser sandbox and has no
// filesystem to log to; its errors surface via the browser console and the blazor-error-ui banner
// instead. See CLAUDE.md for the exact log location.
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
