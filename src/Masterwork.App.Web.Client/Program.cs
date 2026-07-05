using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddSingleton<IModuleLoader, ModuleLoader>();
builder.Services.AddSingleton<IAssetResolver, AssetResolver>();
builder.Services.AddScoped<GameSessionState>();
builder.Services.AddScoped<ISaveStore, LocalStorageSaveStore>();
builder.Services.AddScoped<IAudioPlayer, JsAudioPlayer>();

await builder.Build().RunAsync();
