using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAppSettingsStore"/>
public sealed class LocalStorageAppSettingsStore(IJSRuntime js) : IAppSettingsStore
{
    /// <summary>Exposed so <c>Masterwork.App.Web.Client/Program.cs</c> can read the same key synchronously, before the host is even built, to apply the saved UI culture ahead of the first render.</summary>
    public const string Key = "masterwork.settings";

    /// <inheritdoc/>
    public async Task<AppSettings> LoadAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", Key);
        return AppSettingsJson.Deserialize(json);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(AppSettings settings) =>
        await js.InvokeVoidAsync("localStorage.setItem", Key, AppSettingsJson.Serialize(settings));
}
