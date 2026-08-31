namespace Masterwork.App.Shared.Services;

/// <summary>
/// Decides whether an installed asset pack can be deleted — pure logic over an already-fetched
/// module list, testable without a real <see cref="IModuleStore"/>/<see cref="IAssetPackStore"/>.
/// Use both to decide whether to show a Delete action in the UI at all, and as a guard immediately
/// before actually calling <see cref="IAssetPackStore.DeleteAsync"/>.
/// </summary>
public static class AssetPackDependencyGuard
{
    /// <summary>
    /// True if at least one module in <paramref name="installedModules"/> declares a dependency on
    /// this exact asset-pack id+version — computed on demand each call by scanning the already
    /// in-memory module list, not a maintained counter that could drift stale.
    /// </summary>
    public static bool HasDependent(IEnumerable<InstalledModule> installedModules, string assetPackId, string version) =>
        installedModules.Any(m => m.Dependencies.Any(d =>
            string.Equals(d.Id, assetPackId, StringComparison.Ordinal) &&
            string.Equals(d.Version, version, StringComparison.Ordinal)));
}
