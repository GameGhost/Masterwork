using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSingleton<IModuleLoader, ModuleLoader>();
builder.Services.AddScoped<GameSessionState>();
builder.Services.AddScoped<ISaveStore, LocalStorageSaveStore>();

await builder.Build().RunAsync();
