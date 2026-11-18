using System.Text.Json;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IAssetPackStore"/>
/// <remarks>
/// Backed by <see cref="FileSystem"/>'s app data directory — same shape as <see cref="FileModuleStore"/>,
/// keyed by id+version together rather than id alone, since multiple versions of one asset pack can
/// be installed side by side. MAUI-only, same placement reasoning as <see cref="FileModuleStore"/>.
/// </remarks>
public sealed class FileAssetPackStore : IAssetPackStore
{
    private static string AssetPacksDir => Path.Combine(FileSystem.AppDataDirectory, "asset-packs");

    private static string IndexPath => Path.Combine(AssetPacksDir, "index.json");

    private static string PackDir(string assetPackId, string version) =>
        Path.Combine(AssetPacksDir, $"{Sanitize(assetPackId)}__{Sanitize(version)}");

    private static string Sanitize(string value) =>
        new(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledAssetPack>> ListAsync() => await ReadIndexAsync();

    /// <inheritdoc/>
    public async Task<AssetPackPackageContents?> LoadContentAsync(string assetPackId, string version)
    {
        var dir = PackDir(assetPackId, version);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var manifestPath = Path.Combine(dir, "manifest.yaml");
        var manifestYaml = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath) : null;

        var variablesPath = Path.Combine(dir, "_variables.yaml");
        var variablesYaml = File.Exists(variablesPath) ? await File.ReadAllTextAsync(variablesPath) : null;

        var restextByLocale = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.restext"))
        {
            restextByLocale[Path.GetFileNameWithoutExtension(file)] = await File.ReadAllTextAsync(file);
        }

        var layoutsDir = Path.Combine(dir, "layouts");
        var layoutYamls = new List<string>();
        if (Directory.Exists(layoutsDir))
        {
            foreach (var file in Directory.EnumerateFiles(layoutsDir, "*.mws.yaml"))
            {
                layoutYamls.Add(await File.ReadAllTextAsync(file));
            }
        }

        // Assets deliberately empty here — this method resolves restext/layout-chrome, not asset
        // files (a separate follow-up: wiring IAssetResolver's dependency-pack tier to a real pack).
        return new AssetPackPackageContents(manifestYaml, variablesYaml, restextByLocale, layoutYamls, new Dictionary<string, byte[]>());
    }

    /// <inheritdoc/>
    public async Task<InstalledAssetPack> InstallAsync(
        byte[] mwassetsBytes, string sha256, AssetPackInstallSource source, IProgress<(int Done, int Total)>? progress = null)
    {
        var manifestYaml = AssetPackPackage.ReadManifestOnly(mwassetsBytes)
            ?? throw new InvalidOperationException("This .mwassets file has no manifest.yaml and can't be installed.");
        var manifest = new AssetPackManifestParser().Parse(manifestYaml);

        var dir = PackDir(manifest.Id, manifest.Version);
        if (Directory.Exists(dir))
        {
            // Same accepted-risk reasoning as FileModuleStore.InstallAsync: no staging/swap, a
            // re-install at this exact id+version deletes the old directory first.
            Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(dir);

        await AssetPackPackage.ExtractEntriesAsync(mwassetsBytes, async entry =>
        {
            var destPath = Path.Combine(dir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await File.WriteAllBytesAsync(destPath, entry.Bytes);
        }, progress);

        var entryRecord = new InstalledAssetPack(manifest.Id, manifest.Version, manifest.Title, sha256, source);
        var index = await ReadIndexAsync();
        var updated = index
            .Where(p => !(p.AssetPackId == entryRecord.AssetPackId && p.Version == entryRecord.Version))
            .Append(entryRecord)
            .ToList();
        await WriteIndexAsync(updated);

        return entryRecord;
    }

    /// <inheritdoc/>
    public Task<IModuleAssetSource> GetAssetSourceAsync(string assetPackId, string version) =>
        Task.FromResult<IModuleAssetSource>(new FileModuleAssetSource(PackDir(assetPackId, version)));

    /// <inheritdoc/>
    public async Task DeleteAsync(string assetPackId, string version)
    {
        var dir = PackDir(assetPackId, version);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        var index = await ReadIndexAsync();
        await WriteIndexAsync([.. index.Where(p => !(p.AssetPackId == assetPackId && p.Version == version))]);
    }

    private static async Task<List<InstalledAssetPack>> ReadIndexAsync()
    {
        if (!File.Exists(IndexPath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(IndexPath);
        return JsonSerializer.Deserialize<List<InstalledAssetPack>>(json) ?? [];
    }

    private static async Task WriteIndexAsync(List<InstalledAssetPack> entries)
    {
        Directory.CreateDirectory(AssetPacksDir);
        await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(entries));
    }
}
