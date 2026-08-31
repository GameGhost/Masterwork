namespace Masterwork.App.Shared.Services;

/// <summary>
/// One entry in <see cref="IAssetPackStore"/>'s installed-asset-pack index — mirrors
/// <see cref="InstalledModule"/>'s role for modules. Multiple versions of the same
/// <see cref="AssetPackId"/> can be installed simultaneously, each its own independent record,
/// keyed by the id+version pair, not by id alone.
/// </summary>
public sealed record InstalledAssetPack(
    string AssetPackId,
    string Version,
    string Title,
    string Sha256,
    AssetPackInstallSource InstallSource
);
