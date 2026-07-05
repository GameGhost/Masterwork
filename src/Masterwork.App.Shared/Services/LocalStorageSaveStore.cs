using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="ISaveStore"/>
/// <remarks>Backed by the browser's <c>localStorage</c> and a small JS module (<c>wwwroot/saveExport.js</c>) for triggering file downloads. Registered for the web heads only.</remarks>
public sealed class LocalStorageSaveStore(IJSRuntime js) : ISaveStore
{
    private const string KeyPrefix = "masterwork.save.";

    /// <inheritdoc/>
    public async Task SaveAsync(int slot, string json) =>
        await js.InvokeVoidAsync("localStorage.setItem", KeyPrefix + slot, json);

    /// <inheritdoc/>
    public async Task<string?> LoadAsync(int slot) =>
        await js.InvokeAsync<string?>("localStorage.getItem", KeyPrefix + slot);

    /// <inheritdoc/>
    public async Task<bool> HasSaveAsync(int slot) => await LoadAsync(slot) is not null;

    /// <inheritdoc/>
    public async Task ExportAsync(int slot, string fileName, string json)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/saveExport.js");
        await module.InvokeVoidAsync("downloadTextFile", fileName, json);
    }
}
