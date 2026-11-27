using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>An installed voice pack: its manifest and its files.</summary>
public sealed record InstalledVoicePack(VoicePackManifest Manifest, IModuleAssetSource Assets);

/// <summary>The voice packs installed on this device, as playback reads them.</summary>
public interface IInstalledVoicePacks
{
    /// <summary>Every installed pack whose <c>for:</c> names <paramref name="moduleId"/>, whatever its minimum version.</summary>
    Task<IReadOnlyList<InstalledVoicePack>> ListForModuleAsync(string moduleId);

    /// <summary>Every installed pack.</summary>
    Task<IReadOnlyList<InstalledVoicePack>> ListAsync();
}

/// <summary>
/// Where voice packs are installed. One version of each pack id is installed at a time: installing
/// a pack whose id is already installed replaces it. Whether an install is allowed at all is the
/// caller's decision (<see cref="VoicePackInstallRules"/>).
/// </summary>
public interface IVoicePackStore : IInstalledVoicePacks
{
    /// <summary>Installs a voice pack package, keyed by its manifest's id.</summary>
    /// <exception cref="InvalidOperationException">The package has no <c>manifest.yaml</c>.</exception>
    Task<InstalledVoicePack> InstallAsync(byte[] packageBytes, IProgress<(int Done, int Total)>? progress = null);

    /// <summary>Uninstalls the pack with id <paramref name="packId"/>, if installed.</summary>
    Task DeleteAsync(string packId);
}

/// <summary>Shared by the voice pack stores.</summary>
public static class VoicePackStores
{
    /// <summary>Reads and parses a voice pack package's manifest.</summary>
    /// <exception cref="InvalidOperationException">The package has no <c>manifest.yaml</c>.</exception>
    public static (string Yaml, VoicePackManifest Manifest) ReadManifest(byte[] packageBytes)
    {
        var yaml = AssetPackPackage.ReadManifestOnly(packageBytes)
            ?? throw new InvalidOperationException("This voice pack has no manifest.yaml and can't be installed.");
        return (yaml, new VoicePackManifestParser().Parse(yaml));
    }
}
