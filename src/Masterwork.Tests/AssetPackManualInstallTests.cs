using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class AssetPackManualInstallTests
{
    [Fact]
    public void Decide_NothingInstalledAtThisIdVersion_InstallNew()
    {
        var decision = AssetPackManualInstall.Decide(existing: null, uploadedSha256: "abc123");

        Assert.Equal(AssetPackManualInstallDecision.InstallNew, decision);
    }

    [Fact]
    public void Decide_ExistingHashMatches_PromoteSilently()
    {
        var existing = new InstalledAssetPack("MFW_Common_Assets", "1.0.0", "MFW Common Assets", "abc123", AssetPackInstallSource.Auto);

        var decision = AssetPackManualInstall.Decide(existing, uploadedSha256: "abc123");

        Assert.Equal(AssetPackManualInstallDecision.PromoteSilently, decision);
    }

    [Fact]
    public void Decide_ExistingHashMatches_CaseInsensitive_StillPromotesSilently()
    {
        var existing = new InstalledAssetPack("MFW_Common_Assets", "1.0.0", "MFW Common Assets", "ABC123", AssetPackInstallSource.Auto);

        var decision = AssetPackManualInstall.Decide(existing, uploadedSha256: "abc123");

        Assert.Equal(AssetPackManualInstallDecision.PromoteSilently, decision);
    }

    [Fact]
    public void Decide_ExistingHashDiffers_ConfirmHashMismatch()
    {
        var existing = new InstalledAssetPack("MFW_Common_Assets", "1.0.0", "MFW Common Assets", "abc123", AssetPackInstallSource.Auto);

        var decision = AssetPackManualInstall.Decide(existing, uploadedSha256: "def456");

        Assert.Equal(AssetPackManualInstallDecision.ConfirmHashMismatch, decision);
    }

    [Fact]
    public void Decide_ExistingWasAlreadyManual_HashMatch_StillPromotesSilently()
    {
        // "Promotion" only really matters for an Auto-sourced existing record, but re-installing
        // identical content over an already-Manual one is just as harmless a no-prompt path.
        var existing = new InstalledAssetPack("MFW_Common_Assets", "1.0.0", "MFW Common Assets", "abc123", AssetPackInstallSource.Manual);

        var decision = AssetPackManualInstall.Decide(existing, uploadedSha256: "abc123");

        Assert.Equal(AssetPackManualInstallDecision.PromoteSilently, decision);
    }
}
