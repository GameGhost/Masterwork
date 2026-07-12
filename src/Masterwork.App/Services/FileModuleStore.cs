using System.Text.Json;
using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IModuleStore"/>
/// <remarks>Uploaded packages are backed by <see cref="FileSystem"/>'s app data directory. MAUI-only — lives in the MAUI head rather than the platform-agnostic Shared project.</remarks>
public sealed class FileModuleStore(IModuleLoader loader) : IModuleStore
{
    private static string ModulesDir => Path.Combine(FileSystem.AppDataDirectory, "modules");

    private static string IndexPath => Path.Combine(ModulesDir, "index.json");

    private static string PackagePath(string moduleId) => Path.Combine(ModulesDir, $"{Sanitize(moduleId)}.mwm");

    private static string Sanitize(string id) =>
        new(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledModule>> ListAsync()
    {
        var uploaded = await ReadIndexAsync();
        return [BuiltInModules.Demo, .. uploaded];
    }

    /// <inheritdoc/>
    public async Task<LoadedModuleContent> LoadAsync(string moduleId, string? locale = null)
    {
        if (moduleId == BuiltInModules.DemoModuleId)
        {
            return BuiltInModules.LoadDemo(loader, locale);
        }

        var path = PackagePath(moduleId);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Module '{moduleId}' is not installed.");
        }

        var bytes = await File.ReadAllBytesAsync(path);
        var contents = ModulePackage.ReadFromBytes(bytes);
        var resolvedLocale = ModuleLocales.SelectLocale(contents.RestextByLocale, locale);
        var restext = resolvedLocale is not null ? contents.RestextByLocale[resolvedLocale] : null;
        var restextOverride = resolvedLocale is not null
            ? contents.RestextOverridesByLocale.GetValueOrDefault(resolvedLocale)
            : null;
        var module = loader.LoadFromSources(
            contents.PassageYamls, contents.VariablesYaml, restext, contents.OverridePassageYamls, restextOverride,
            contents.LayoutYamls);
        return LoadedModuleContent.FromPackage(contents, module);
    }

    /// <inheritdoc/>
    public async Task<InstalledModule> InstallAsync(byte[] mwmBytes)
    {
        var contents = ModulePackage.ReadFromBytes(mwmBytes);
        if (contents.ManifestYaml is null)
        {
            throw new InvalidOperationException("This .mwm file has no manifest.yaml and can't be installed.");
        }

        var manifest = new ManifestParser().Parse(contents.ManifestYaml);
        var languages = ModuleLocales.SortedLocales(contents.RestextByLocale);

        Directory.CreateDirectory(ModulesDir);
        await File.WriteAllBytesAsync(PackagePath(manifest.Id), mwmBytes);

        var entry = new InstalledModule(manifest.Id, manifest.Version, manifest.Title, manifest.Description ?? "", IsBuiltIn: false, languages);
        var index = await ReadIndexAsync();
        var updated = index.Where(m => m.ModuleId != entry.ModuleId).Append(entry).ToList();
        await WriteIndexAsync(updated);

        return entry;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetPackageBytesAsync(string moduleId)
    {
        if (moduleId == BuiltInModules.DemoModuleId)
        {
            return null;
        }

        var path = PackagePath(moduleId);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string moduleId)
    {
        if (moduleId == BuiltInModules.DemoModuleId)
        {
            throw new InvalidOperationException("The built-in demo module can't be removed.");
        }

        var path = PackagePath(moduleId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var index = await ReadIndexAsync();
        await WriteIndexAsync([.. index.Where(m => m.ModuleId != moduleId)]);
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
