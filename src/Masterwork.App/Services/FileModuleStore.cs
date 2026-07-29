using System.Text.Json;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IModuleStore"/>
/// <remarks>
/// Backed by <see cref="FileSystem"/>'s app data directory. A module is stored as a directory of
/// loose files mirroring its <c>.mwm</c> zip's own layout (<c>manifest.yaml</c>, <c>passages/</c>,
/// <c>assets/</c>, ...) rather than one packed file — so loading a passage's assets is a plain
/// <see cref="File.ReadAllBytesAsync(string, System.Threading.CancellationToken)"/> per asset instead
/// of decompressing the whole package first. Locale selection across a module's <c>*.restext</c>
/// files is done inline here (app-layer only) rather than through
/// <see cref="Masterwork.ModuleFormat.IModuleLoader.LoadFromDirectory"/>, since that method only
/// picks a single fixed culture. MAUI-only — lives in the MAUI head rather than the
/// platform-agnostic Shared project.
/// </remarks>
public sealed class FileModuleStore(IModuleLoader loader) : IModuleStore
{
    private static string ModulesDir => Path.Combine(FileSystem.AppDataDirectory, "modules");

    private static string IndexPath => Path.Combine(ModulesDir, "index.json");

    private static string ModuleDir(string moduleId) => Path.Combine(ModulesDir, Sanitize(moduleId));

    private static string Sanitize(string id) =>
        new(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledModule>> ListAsync() => await ReadIndexAsync();

    /// <inheritdoc/>
    public async Task<LoadedModuleContent> LoadAsync(string moduleId, string? locale = null)
    {
        var moduleDir = ModuleDir(moduleId);
        if (!Directory.Exists(moduleDir))
        {
            throw new InvalidOperationException($"Module '{moduleId}' is not installed.");
        }

        var manifestPath = Path.Combine(moduleDir, "manifest.yaml");
        var manifestYaml = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath) : null;

        var variablesPath = Path.Combine(moduleDir, "_variables.yaml");
        var variablesYaml = File.Exists(variablesPath) ? await File.ReadAllTextAsync(variablesPath) : null;

        var passagesDir = Path.Combine(moduleDir, "passages");
        var passageYamls = Directory.Exists(passagesDir)
            ? await ReadAllTextFilesAsync(passagesDir, "*.mws.yaml")
            : [];

        var overrideDir = Path.Combine(moduleDir, "passages-override");
        var overridePassageYamls = Directory.Exists(overrideDir)
            ? await ReadAllTextFilesAsync(overrideDir, "*.mws.yaml")
            : [];

        var layoutsDir = Path.Combine(moduleDir, "layouts");
        var layoutYamls = Directory.Exists(layoutsDir)
            ? await ReadAllTextFilesAsync(layoutsDir, "*.mws.yaml")
            : [];

        var additionalVariablesDir = Path.Combine(moduleDir, "variables");
        var additionalVariableYamls = Directory.Exists(additionalVariablesDir)
            ? await ReadAllTextFilesAsync(additionalVariablesDir, "*.yaml")
            : [];

        var (restextByLocale, restextOverridesByLocale) = ReadRestextFiles(moduleDir);
        var resolvedLocale = ModuleLocales.SelectLocale(restextByLocale, locale);
        var restext = resolvedLocale is not null ? restextByLocale[resolvedLocale] : null;
        var restextOverride = resolvedLocale is not null
            ? restextOverridesByLocale.GetValueOrDefault(resolvedLocale)
            : null;

        var module = loader.LoadFromSources(
            passageYamls, variablesYaml, restext, overridePassageYamls, restextOverride, layoutYamls, additionalVariableYamls);

        var assets = new FileModuleAssetSource(moduleDir);
        return await LoadedModuleContent.BuildAsync(module, manifestYaml, assets);
    }

    /// <inheritdoc/>
    public async Task<InstalledModule> InstallAsync(byte[] mwmBytes, string sha256, IProgress<(int Done, int Total)>? progress = null)
    {
        var manifestYaml = ModulePackage.ReadManifestOnly(mwmBytes)
            ?? throw new InvalidOperationException("This .mwm file has no manifest.yaml and can't be installed.");
        var manifest = new ManifestParser().Parse(manifestYaml);

        var moduleDir = ModuleDir(manifest.Id);
        if (Directory.Exists(moduleDir))
        {
            // No staging/swap — a re-install of an already-installed id deletes its old directory
            // first (accepted risk: an interrupted install leaves a half-written module, recoverable
            // by re-uploading), so a re-upload that dropped a file doesn't leave the old copy behind.
            Directory.Delete(moduleDir, recursive: true);
        }
        Directory.CreateDirectory(moduleDir);

        await ModulePackage.ExtractEntriesAsync(mwmBytes, async entry =>
        {
            var destPath = Path.Combine(moduleDir, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await File.WriteAllBytesAsync(destPath, entry.Bytes);
        }, progress);

        var (restextByLocale, _) = ReadRestextFiles(moduleDir);
        var languages = ModuleLocales.SortedLocales(restextByLocale);

        // Resolved once here, not re-resolved on every ListAsync — unlike IndexedDbModuleStore's blob:
        // URLs (which don't survive past the browser session that created them), FileModuleAssetSource
        // returns a plain data: URI, which is fine to bake into index.json and read back verbatim for
        // as long as the module stays installed.
        var thumbnailAssets = new FileModuleAssetSource(moduleDir);
        var thumbnailUrl = await ModuleThumbnailResolver.ResolveAsync(thumbnailAssets, manifest.Thumbnail?.Image);

        var entryRecord = new InstalledModule(manifest.Id, manifest.Version, manifest.Title, manifest.Description ?? "", languages, sha256, thumbnailUrl);
        var index = await ReadIndexAsync();
        var updated = index.Where(m => m.ModuleId != entryRecord.ModuleId).Append(entryRecord).ToList();
        await WriteIndexAsync(updated);

        return entryRecord;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string moduleId)
    {
        var moduleDir = ModuleDir(moduleId);
        if (Directory.Exists(moduleDir))
        {
            Directory.Delete(moduleDir, recursive: true);
        }

        var index = await ReadIndexAsync();
        await WriteIndexAsync([.. index.Where(m => m.ModuleId != moduleId)]);
    }

    private static async Task<List<string>> ReadAllTextFilesAsync(string dir, string searchPattern)
    {
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, searchPattern))
        {
            result.Add(await File.ReadAllTextAsync(file));
        }

        return result;
    }

    // Mirrors ModuleLoader.LoadFromDirectory's own restext-file convention (a "{culture}.restext"
    // base file with an optional sibling "{culture}.overrides.restext"), except every locale present
    // is read rather than just one — FileModuleStore does its own locale selection afterward via
    // ModuleLocales.SelectLocale, since IModuleLoader's public surface only supports a single fixed
    // culture per load.
    private static (Dictionary<string, string> ByLocale, Dictionary<string, string> OverridesByLocale) ReadRestextFiles(string moduleDir)
    {
        var byLocale = new Dictionary<string, string>();
        var overridesByLocale = new Dictionary<string, string>();
        if (!Directory.Exists(moduleDir))
        {
            return (byLocale, overridesByLocale);
        }

        foreach (var file in Directory.EnumerateFiles(moduleDir, "*.restext"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".overrides.restext", StringComparison.OrdinalIgnoreCase))
            {
                overridesByLocale[fileName[..^".overrides.restext".Length]] = File.ReadAllText(file);
            }
            else
            {
                byLocale[fileName[..^".restext".Length]] = File.ReadAllText(file);
            }
        }

        return (byLocale, overridesByLocale);
    }

    private static async Task<List<InstalledModule>> ReadIndexAsync()
    {
        if (!File.Exists(IndexPath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(IndexPath);
        return JsonSerializer.Deserialize<List<InstalledModule>>(json) ?? [];
    }

    private static async Task WriteIndexAsync(List<InstalledModule> entries)
    {
        Directory.CreateDirectory(ModulesDir);
        await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(entries));
    }
}
