using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSingleton<IModuleLoader, ModuleLoader>();
builder.Services.AddScoped<GameSessionState>();

await builder.Build().RunAsync();
