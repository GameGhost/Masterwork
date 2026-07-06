using System.Text.Json;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAppSettingsStore"/>
public sealed class LocalStorageAppSettingsStore(IJSRuntime js) : IAppSettingsStore
{
    private const string Key = "masterwork.settings";

    /// <inheritdoc/>
    public async Task<AppSettings> LoadAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        return json is null ? AppSettings.Default : JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(AppSettings settings) =>
        await js.InvokeVoidAsync("localStorage.setItem", Key, JsonSerializer.Serialize(settings));
}
