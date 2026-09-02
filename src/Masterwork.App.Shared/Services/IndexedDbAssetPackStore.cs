using Masterwork.ModuleFormat;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAssetPackStore"/>
/// <remarks>
/// Backed by its own IndexedDB database (<c>wwwroot/assetPackStore.js</c>), separate from
/// <see cref="IndexedDbModuleStore"/>'s — an asset pack is keyed by id+version together, since
/// multiple versions can be installed side by side. Registered for the web heads only.
/// </remarks>
public sealed class IndexedDbAssetPackStore(IJSRuntime js) : IAssetPackStore
{
    // Mirrors assetPackStore.js's assetPackMeta record shape.
    private sealed record AssetPackMetaRecord(
        string Id,
        string Version,
        string Title,
        string Sha256,
        string InstallSource,
        string? ManifestYaml,
        string? VariablesYaml,
        Dictionary<string, string> RestextByLocale,
        string[] LayoutYamls
    );

    private async Task<IJSObjectReference> ModuleAsync() =>
        await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/assetPackStore.js");

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledAssetPack>> ListAsync()
    {
        var jsModule = await ModuleAsync();
        var rows = await jsModule.InvokeAsync<AssetPackMetaRecord[]>("listAssetPackMeta");
        return [.. rows.Select(ToInstalledAssetPack)];
    }

    /// <inheritdoc/>
    public async Task<AssetPackPackageContents?> LoadContentAsync(string assetPackId, string version)
    {
        var jsModule = await ModuleAsync();
        var record = await jsModule.InvokeAsync<AssetPackMetaRecord?>("getAssetPackMeta", assetPackId, version);
        if (record is null)
        {
            return null;
        }

        // Assets deliberately empty here — same reasoning as FileAssetPackStore.LoadContentAsync;
        // this is the restext/layout-chrome load-path wiring, not asset resolution.
        return new AssetPackPackageContents(record.ManifestYaml, record.VariablesYaml, record.RestextByLocale, record.LayoutYamls, new Dictionary<string, byte[]>());
    }

    /// <inheritdoc/>
    public async Task<InstalledAssetPack> InstallAsync(
        byte[] mwassetsBytes, string sha256, AssetPackInstallSource source, IProgress<(int Done, int Total)>? progress = null)
    {
        var manifestYaml = AssetPackPackage.ReadManifestOnly(mwassetsBytes)
            ?? throw new InvalidOperationException("This .mwassets file has no manifest.yaml and can't be installed.");
        var manifest = new AssetPackManifestParser().Parse(manifestYaml);

        string? variablesYaml = null;
        var layoutYamls = new List<string>();
        var restextByLocale = new Dictionary<string, string>();

        var jsModule = await ModuleAsync();

        // No staging/swap — same accepted-risk reasoning as IndexedDbModuleStore.InstallAsync.
        await jsModule.InvokeVoidAsync("clearAssetPack", manifest.Id, manifest.Version);

        await AssetPackPackage.ExtractEntriesAsync(mwassetsBytes, async entry =>
        {
            switch (entry.Kind)
            {
                case AssetPackPackageEntryKind.Manifest:
                    break; // already have it from ReadManifestOnly
                case AssetPackPackageEntryKind.Variables:
                    variablesYaml = System.Text.Encoding.UTF8.GetString(entry.Bytes);
                    break;
                case AssetPackPackageEntryKind.Restext:
                    restextByLocale[entry.Locale!] = System.Text.Encoding.UTF8.GetString(entry.Bytes);
                    break;
                case AssetPackPackageEntryKind.Layout:
                    layoutYamls.Add(System.Text.Encoding.UTF8.GetString(entry.Bytes));
                    break;
                case AssetPackPackageEntryKind.Asset:
                    await jsModule.InvokeVoidAsync("putAssetPackAsset", manifest.Id, manifest.Version, entry.Path, entry.Bytes);
                    break;
            }
        }, progress);

        var record = new AssetPackMetaRecord(
            manifest.Id, manifest.Version, manifest.Title, sha256, source.ToString(),
            manifestYaml, variablesYaml, restextByLocale, [.. layoutYamls]);
        await jsModule.InvokeVoidAsync("putAssetPackMeta", record);

        return ToInstalledAssetPack(record);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string assetPackId, string version)
    {
        var jsModule = await ModuleAsync();
        await jsModule.InvokeVoidAsync("clearAssetPack", assetPackId, version);
    }

    /// <inheritdoc/>
    public async Task<IModuleAssetSource> GetAssetSourceAsync(string assetPackId, string version) =>
        new IndexedDbAssetPackAssetSource(await ModuleAsync(), assetPackId, version);

    private static InstalledAssetPack ToInstalledAssetPack(AssetPackMetaRecord r) =>
        new(r.Id, r.Version, r.Title, r.Sha256, Enum.Parse<AssetPackInstallSource>(r.InstallSource));
}
