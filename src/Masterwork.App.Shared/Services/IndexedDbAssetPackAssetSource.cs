using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleAssetSource"/>
/// <remarks>
/// Resolves one asset at a time from the <c>assetPackAssets</c> IndexedDB store
/// (<c>wwwroot/assetPackStore.js</c>) — same shape and reasoning as
/// <see cref="IndexedDbModuleAssetSource"/>, keyed by id+version together instead of a single module
/// id, since multiple versions of one asset pack can be installed side by side.
/// </remarks>
public sealed class IndexedDbAssetPackAssetSource(IJSObjectReference jsModule, string assetPackId, string version) : IModuleAssetSource
{
    /// <inheritdoc/>
    public async Task<byte[]?> GetAssetAsync(string assetPath) =>
        await jsModule.InvokeAsync<byte[]?>("getAssetPackAsset", assetPackId, version, assetPath);

    /// <inheritdoc/>
    public async Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) =>
        await jsModule.InvokeAsync<string?>("getAssetPackAssetAsObjectUrl", assetPackId, version, assetPath, mimeType);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListAssetPathsAsync() =>
        await jsModule.InvokeAsync<string[]>("listAssetPackAssetPaths", assetPackId, version);
}
