using Masterwork.App.Shared;
using Masterwork.App.Shared.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBrowserHostServices();

// The flavor this build was compiled as (WhiteLabelFlavor): registers its WhiteLabelConfig and theme
// services.
builder.Services.AddThemeModule(typeof(Program).Assembly);

var host = builder.Build();

// Before RunAsync starts the renderer, so the first paint is already in the player's language.
BrowserHost.ApplyStartupCulture(host.Services.GetRequiredService<IJSRuntime>());

await host.RunAsync();
