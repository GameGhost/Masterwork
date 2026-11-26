using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IVoicePackStore"/>
/// <remarks>
/// One folder per pack under the app data directory, holding the extracted package. The folders'
/// own <c>manifest.yaml</c> files are the index, so there is nothing else to keep in step.
/// </remarks>
public sealed class FileVoicePackStore : IVoicePackStore
{
    private static string VoicePacksDir => Path.Combine(FileSystem.AppDataDirectory, "voice-packs");

    // A readable name plus a hash of the exact id, so two ids that sanitize alike never share a folder.
    private static string PackDir(string packId) =>
        Path.Combine(VoicePacksDir,
            new string([.. packId.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_')])
            + "__" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(packId)))[..8].ToLowerInvariant());

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledVoicePack>> ListAsync()
    {
        if (!Directory.Exists(VoicePacksDir))
        {
            return [];
        }

        var packs = new List<InstalledVoicePack>();
        foreach (var dir in Directory.EnumerateDirectories(VoicePacksDir))
        {
            var manifestPath = Path.Combine(dir, "manifest.yaml");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                var manifest = new VoicePackManifestParser().Parse(await File.ReadAllTextAsync(manifestPath));
                packs.Add(new InstalledVoicePack(manifest, new FileModuleAssetSource(dir)));
            }
            catch (Exception ex) when (ex is MwsParseException or YamlDotNet.Core.YamlException or IOException)
            {
                // A pack that can't be read just isn't offered; the others still are.
            }
        }

        return packs;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledVoicePack>> ListForModuleAsync(string moduleId) =>
        [.. (await ListAsync()).Where(p => p.Manifest.For.ModuleId == moduleId)];

    /// <inheritdoc/>
    public async Task<InstalledVoicePack> InstallAsync(byte[] packageBytes, IProgress<(int Done, int Total)>? progress = null)
    {
        var (yaml, manifest) = VoicePackStores.ReadManifest(packageBytes);
        var dir = PackDir(manifest.Id);

        // No staging: an update replaces the old copy outright, as the module stores do.
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        Directory.CreateDirectory(dir);
        await AssetPackPackage.ExtractEntriesAsync(packageBytes, async entry =>
        {
            if (entry.Kind != AssetPackPackageEntryKind.Asset)
            {
                return;
            }

            var destPath = Path.Combine(dir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            await File.WriteAllBytesAsync(destPath, entry.Bytes);
        }, progress);

        // Written last: a folder is listed only once its manifest exists, so an interrupted install never shows up as a pack.
        await File.WriteAllTextAsync(Path.Combine(dir, "manifest.yaml"), yaml);
        return new InstalledVoicePack(manifest, new FileModuleAssetSource(dir));
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string packId)
    {
        var dir = PackDir(packId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        return Task.CompletedTask;
    }
}
