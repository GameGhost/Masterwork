using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleAssetSource"/>
/// <remarks>
/// Resolves one asset at a time from the <c>moduleAssets</c> IndexedDB store
/// (<c>wwwroot/moduleStore.js</c>'s <c>getModuleAssetAsObjectUrl</c>) entirely JS-side: the read,
/// <c>Blob</c> construction, and <c>URL.createObjectURL</c> call all happen in JavaScript, and only
/// the resulting (short) <c>blob:</c> URL string crosses back into .NET — the asset's actual bytes
/// never do. This matters: per-asset <c>byte[]</c> JSInterop marshaling (the previous design) was
/// measured as the dominant cost behind multi-second stalls and multi-hundred-MB memory spikes when
/// preloading a module with many sizable images. Constructed fresh by
/// <see cref="IndexedDbModuleStore.LoadAsync"/> for each loaded module; holds only a JS module handle
/// and the module id, no asset bytes of its own. Object URLs this creates must be revoked
/// (<see cref="PreloadedModuleAssetSource.RevokeAllAsync"/>) once the module unloads, or the browser
/// keeps their backing data alive for the tab's remaining lifetime.
/// </remarks>
public sealed class IndexedDbModuleAssetSource(IJSObjectReference jsModule, string moduleId) : IModuleAssetSource
{
    /// <inheritdoc/>
    public async Task<byte[]?> GetAssetAsync(string assetPath) =>
        await jsModule.InvokeAsync<byte[]?>("getModuleAsset", moduleId, assetPath);

    /// <inheritdoc/>
    public async Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) =>
        await jsModule.InvokeAsync<string?>("getModuleAssetAsObjectUrl", moduleId, assetPath, mimeType);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListAssetPathsAsync() =>
        await jsModule.InvokeAsync<string[]>("listModuleAssetPaths", moduleId);
}
