using System.Text.Json;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="ISaveStore"/>
/// <remarks>
/// Backed by the browser's <c>localStorage</c> and a small JS module (<c>wwwroot/saveExport.js</c>)
/// for triggering file downloads. Registered for the web heads only. The full list of
/// <see cref="SaveEntry"/> metadata is kept as one JSON array under <see cref="IndexKey"/> — plain
/// <c>localStorage</c> has no key-enumeration API worth round-tripping through JS interop for, so
/// an explicit index is simpler than scanning for a key prefix.
/// </remarks>
public sealed class LocalStorageSaveStore(IJSRuntime js) : ISaveStore
{
    private const string IndexKey = "masterwork.saves.index";
    private const string DataKeyPrefix = "masterwork.save.data.";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SaveEntry>> ListAsync() => await ReadIndexAsync();

    /// <inheritdoc/>
    public async Task<string?> LoadAsync(string id) =>
        await js.InvokeAsync<string?>("localStorage.getItem", DataKeyPrefix + id);

    /// <inheritdoc/>
    public async Task SaveAsync(SaveEntry entry, string json)
    {
        await js.InvokeVoidAsync("localStorage.setItem", DataKeyPrefix + entry.Id, json);
        var index = await ReadIndexAsync();
        var updated = index.Where(e => e.Id != entry.Id).Append(entry).ToList();
        await WriteIndexAsync(updated);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string id)
    {
        await js.InvokeVoidAsync("localStorage.removeItem", DataKeyPrefix + id);
        var index = await ReadIndexAsync();
        await WriteIndexAsync([.. index.Where(e => e.Id != id)]);
    }

    /// <inheritdoc/>
    public async Task ExportAsync(string id, string fileName, string json)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/saveExport.js");
        await module.InvokeVoidAsync("downloadTextFile", fileName, json);
    }

    private async Task<List<SaveEntry>> ReadIndexAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", IndexKey);
        return json is null ? [] : JsonSerializer.Deserialize<List<SaveEntry>>(json) ?? [];
    }

    private async Task WriteIndexAsync(List<SaveEntry> entries) =>
        await js.InvokeVoidAsync("localStorage.setItem", IndexKey, JsonSerializer.Serialize(entries));
}
