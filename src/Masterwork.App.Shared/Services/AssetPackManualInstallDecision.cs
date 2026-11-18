namespace Masterwork.App.Shared.Services;

/// <summary>What a manual <c>.mwassets</c> upload should do next, given whatever's already installed at the same id+version — see <see cref="AssetPackManualInstall.Decide"/>.</summary>
public enum AssetPackManualInstallDecision
{
    /// <summary>Nothing installed at this id+version yet — install directly as <see cref="AssetPackInstallSource.Manual"/>.</summary>
    InstallNew,

    /// <summary>Already installed with an identical hash — install directly as <see cref="AssetPackInstallSource.Manual"/> (a same-content re-install/promotion), no prompt needed.</summary>
    PromoteSilently,

    /// <summary>Already installed with a <em>different</em> hash at the same declared id+version — show an "install anyway" confirm before overwriting.</summary>
    ConfirmHashMismatch,
}

/// <summary>
/// Decides what a manual <c>.mwassets</c> install should do, given the asset pack already installed
/// (if any) at the same id+version — pure logic, no I/O, testable without a real
/// <see cref="IAssetPackStore"/>. Mirrors <c>StartNewGame.razor</c>'s existing same-id conflict-check
/// for modules, minus the version-upgrade/downgrade reasoning that needs — an asset pack's version
/// is part of its install key, so two different versions are just two independent records, never a
/// conflict to arbitrate.
/// </summary>
public static class AssetPackManualInstall
{
    /// <param name="existing">The currently-installed record at the upload's declared id+version, or <see langword="null"/> if nothing is installed there yet.</param>
    /// <param name="uploadedSha256">The manually-uploaded package's own computed hash.</param>
    public static AssetPackManualInstallDecision Decide(InstalledAssetPack? existing, string uploadedSha256)
    {
        if (existing is null)
        {
            return AssetPackManualInstallDecision.InstallNew;
        }

        return string.Equals(existing.Sha256, uploadedSha256, StringComparison.OrdinalIgnoreCase)
            ? AssetPackManualInstallDecision.PromoteSilently
            : AssetPackManualInstallDecision.ConfirmHashMismatch;
    }
}
