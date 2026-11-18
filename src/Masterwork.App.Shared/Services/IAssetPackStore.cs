using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The app's installed-asset-pack registry — mirrors <see cref="IModuleStore"/>'s shape: a
/// MAUI-specific file-backed implementation and a web IndexedDB-backed one each implement this for
/// their respective hosts. Multiple versions of the same asset pack id can be installed
/// simultaneously — every operation here is keyed by the exact id+version pair, never id alone.
/// </summary>
/// <remarks>
/// This store has no opinion on two decisions that live one layer up, in the caller (module store
/// install/delete flow and its UI):
/// <list type="bullet">
/// <item>
/// <b>Auto→Manual promotion</b>: <see cref="InstallAsync"/> always installs unconditionally,
/// overwriting whatever was at that exact id+version before, including its
/// <see cref="AssetPackInstallSource"/> — calling it with <see cref="AssetPackInstallSource.Manual"/>
/// over an existing auto-installed copy is how promotion happens. Whether to do that silently or
/// after a confirm prompt is the caller's decision — see <see cref="AssetPackManualInstallDecision"/>.
/// </item>
/// <item>
/// <b>Delete guard</b>: <see cref="DeleteAsync"/> does not check whether any installed module still
/// depends on this asset pack — the caller must check first via <see cref="AssetPackDependencyGuard"/>.
/// </item>
/// </list>
/// </remarks>
public interface IAssetPackStore
{
    /// <summary>All asset packs currently installed, across every id and version.</summary>
    Task<IReadOnlyList<InstalledAssetPack>> ListAsync();

    /// <summary>
    /// Loads one installed asset pack's full content by its exact id+version, for a dependent
    /// module's load path to fold in. Returns <see langword="null"/> if nothing is installed at
    /// that exact id+version.
    /// </summary>
    Task<AssetPackPackageContents?> LoadContentAsync(string assetPackId, string version);

    /// <summary>
    /// Installs a <c>.mwassets</c> package from raw bytes, keyed by its manifest's declared id and
    /// version together. Unconditionally overwrites whatever was previously installed at that exact
    /// id+version, including its <see cref="AssetPackInstallSource"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The package has no <c>manifest.yaml</c>.</exception>
    Task<InstalledAssetPack> InstallAsync(
        byte[] mwassetsBytes, string sha256, AssetPackInstallSource source, IProgress<(int Done, int Total)>? progress = null);

    /// <summary>Uninstalls one asset pack version. Does not check for dependents — see <see cref="AssetPackDependencyGuard"/>.</summary>
    Task DeleteAsync(string assetPackId, string version);

    /// <summary>
    /// An <see cref="IModuleAssetSource"/> over this asset pack's own <c>assets/</c> subtree, for
    /// <see cref="IAssetResolver"/>'s dependency-pack tier — same shape and lazy-per-file contract as
    /// a module's own asset source, just backed by the pack's installed content instead. Does not
    /// itself check whether the pack is actually installed at this id+version; a lookup against a
    /// nonexistent pack simply resolves every asset to <see langword="null"/>, same as a real pack
    /// missing that one file.
    /// </summary>
    Task<IModuleAssetSource> GetAssetSourceAsync(string assetPackId, string version);
}
