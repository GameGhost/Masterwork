using Masterwork.ModuleFormat;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IVoicePackStore"/>
/// <remarks>Backed by its own IndexedDB database (<c>wwwroot/voicePackStore.js</c>). Registered for the web heads only.</remarks>
public sealed class IndexedDbVoicePackStore(IJSRuntime js) : IVoicePackStore
{
    // Mirrors voicePackStore.js's voicePackMeta record shape.
    private sealed record VoicePackMetaRecord(string Id, string Version, string ManifestYaml);

    private async Task<IJSObjectReference> ModuleAsync() =>
        await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/voicePackStore.js");

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledVoicePack>> ListAsync()
    {
        var jsModule = await ModuleAsync();
        var rows = await jsModule.InvokeAsync<VoicePackMetaRecord[]>("listVoicePackMeta");
        return [.. rows.Select(r => ToInstalled(jsModule, r.Id, new VoicePackManifestParser().Parse(r.ManifestYaml)))];
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledVoicePack>> ListForModuleAsync(string moduleId) =>
        [.. (await ListAsync()).Where(p => p.Manifest.For.ModuleId == moduleId)];

    /// <inheritdoc/>
    public async Task<InstalledVoicePack> InstallAsync(byte[] packageBytes, IProgress<(int Done, int Total)>? progress = null)
    {
        var (yaml, manifest) = VoicePackStores.ReadManifest(packageBytes);
        var jsModule = await ModuleAsync();

        // No staging: an update replaces the old copy outright, as the module stores do.
        await jsModule.InvokeVoidAsync("clearVoicePack", manifest.Id);
        await AssetPackPackage.ExtractEntriesAsync(packageBytes, async entry =>
        {
            if (entry.Kind == AssetPackPackageEntryKind.Asset)
            {
                await jsModule.InvokeVoidAsync("putVoicePackAsset", manifest.Id, entry.Path, entry.Bytes);
            }
        }, progress);
        await jsModule.InvokeVoidAsync("putVoicePackMeta", new VoicePackMetaRecord(manifest.Id, manifest.Version, yaml));

        return ToInstalled(jsModule, manifest.Id, manifest);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string packId)
    {
        var jsModule = await ModuleAsync();
        await jsModule.InvokeVoidAsync("clearVoicePack", packId);
    }

    private static InstalledVoicePack ToInstalled(IJSObjectReference jsModule, string id, VoicePackManifest manifest) =>
        new(manifest, new IndexedDbVoicePackAssetSource(jsModule, id));

    private sealed class IndexedDbVoicePackAssetSource(IJSObjectReference jsModule, string packId) : IModuleAssetSource
    {
        public async Task<byte[]?> GetAssetAsync(string assetPath) =>
            await jsModule.InvokeAsync<byte[]?>("getVoicePackAsset", packId, assetPath);

        public async Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) =>
            await jsModule.InvokeAsync<string?>("getVoicePackAssetAsObjectUrl", packId, assetPath, mimeType);

        public async Task<IReadOnlyList<string>> ListAssetPathsAsync() =>
            await jsModule.InvokeAsync<string[]>("listVoicePackAssetPaths", packId);
    }
}
