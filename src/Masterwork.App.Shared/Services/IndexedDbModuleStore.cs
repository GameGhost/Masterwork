using System.Text;
using Masterwork.ModuleFormat;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleStore"/>
/// <remarks>
/// Backed by two IndexedDB stores (<c>wwwroot/moduleStore.js</c>): <c>moduleMeta</c> holds a
/// module's text (passages, restext, manifest/variables YAML) as one record, and <c>moduleAssets</c>
/// holds one record per binary asset — so loading/installing a module never requires holding its
/// entire decompressed package (including every image) in memory at once. Registered for the web
/// heads only.
/// </remarks>
public sealed class IndexedDbModuleStore(IJSRuntime js, IModuleLoader loader) : IModuleStore
{
    // Mirrors moduleStore.js's moduleMeta record shape; JSInterop's default camelCase naming policy
    // maps these PascalCase properties to/from the store's camelCase JSON keys.
    private sealed record ModuleMetaRecord(
        string Id,
        string Title,
        string Version,
        string? Description,
        string[] Languages,
        string Sha256,
        string? ManifestYaml,
        string? VariablesYaml,
        string[] PassageYamls,
        string[] OverridePassageYamls,
        string[] LayoutYamls,
        string[] AdditionalVariableYamls,
        Dictionary<string, string> RestextByLocale,
        Dictionary<string, string> RestextOverridesByLocale
    );

    private async Task<IJSObjectReference> ModuleAsync() =>
        await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/moduleStore.js");

    /// <inheritdoc/>
    public async Task<IReadOnlyList<InstalledModule>> ListAsync()
    {
        var jsModule = await ModuleAsync();
        var rows = await jsModule.InvokeAsync<ModuleMetaRecord[]>("listModuleMeta");
        return rows.Select(m => new InstalledModule(m.Id, m.Version, m.Title, m.Description ?? "", m.Languages, m.Sha256)).ToList();
    }

    /// <inheritdoc/>
    public async Task<LoadedModuleContent> LoadAsync(string moduleId, string? locale = null)
    {
        var jsModule = await ModuleAsync();
        var meta = await jsModule.InvokeAsync<ModuleMetaRecord?>("getModuleMeta", moduleId)
            ?? throw new InvalidOperationException($"Module '{moduleId}' is not installed.");

        var resolvedLocale = ModuleLocales.SelectLocale(meta.RestextByLocale, locale);
        var restext = resolvedLocale is not null ? meta.RestextByLocale[resolvedLocale] : null;
        var restextOverride = resolvedLocale is not null
            ? meta.RestextOverridesByLocale.GetValueOrDefault(resolvedLocale)
            : null;
        var loadedModule = loader.LoadFromSources(
            meta.PassageYamls, meta.VariablesYaml, restext, meta.OverridePassageYamls, restextOverride,
            meta.LayoutYamls, meta.AdditionalVariableYamls);

        var assets = new IndexedDbModuleAssetSource(jsModule, moduleId);
        return await LoadedModuleContent.BuildAsync(loadedModule, meta.ManifestYaml, assets);
    }

    /// <inheritdoc/>
    public async Task<InstalledModule> InstallAsync(byte[] mwmBytes, string sha256, IProgress<(int Done, int Total)>? progress = null)
    {
        var manifestYaml = ModulePackage.ReadManifestOnly(mwmBytes)
            ?? throw new InvalidOperationException("This .mwm file has no manifest.yaml and can't be installed.");
        var manifest = new ManifestParser().Parse(manifestYaml);

        string? variablesYaml = null;
        var passageYamls = new List<string>();
        var overridePassageYamls = new List<string>();
        var layoutYamls = new List<string>();
        var additionalVariableYamls = new List<string>();
        var restextByLocale = new Dictionary<string, string>();
        var restextOverridesByLocale = new Dictionary<string, string>();

        var jsModule = await ModuleAsync();

        // No staging/swap — a re-install of an already-installed id clears its old record set first
        // (accepted risk: an interrupted install leaves a half-written module, recoverable by
        // re-uploading), so a re-upload that dropped an asset doesn't leave the old copy orphaned.
        await jsModule.InvokeVoidAsync("clearModule", manifest.Id);

        await ModulePackage.ExtractEntriesAsync(mwmBytes, async entry =>
        {
            switch (entry.Kind)
            {
                case ModulePackageEntryKind.Manifest:
                    break; // already have it from ReadManifestOnly
                case ModulePackageEntryKind.Variables:
                    variablesYaml = Encoding.UTF8.GetString(entry.Bytes);
                    break;
                case ModulePackageEntryKind.Restext:
                    restextByLocale[entry.Locale!] = Encoding.UTF8.GetString(entry.Bytes);
                    break;
                case ModulePackageEntryKind.RestextOverride:
                    restextOverridesByLocale[entry.Locale!] = Encoding.UTF8.GetString(entry.Bytes);
                    break;
                case ModulePackageEntryKind.Passage:
                    passageYamls.Add(Encoding.UTF8.GetString(entry.Bytes));
                    break;
                case ModulePackageEntryKind.PassageOverride:
                    overridePassageYamls.Add(Encoding.UTF8.GetString(entry.Bytes));
                    break;
                case ModulePackageEntryKind.Layout:
                    layoutYamls.Add(Encoding.UTF8.GetString(entry.Bytes));
                    break;
                case ModulePackageEntryKind.AdditionalVariable:
                    additionalVariableYamls.Add(Encoding.UTF8.GetString(entry.Bytes));
                    break;
                case ModulePackageEntryKind.Asset:
                    await jsModule.InvokeVoidAsync("putModuleAsset", manifest.Id, entry.Path, entry.Bytes);
                    break;
            }
        }, progress);

        var languages = ModuleLocales.SortedLocales(restextByLocale);

        var meta = new ModuleMetaRecord(
            manifest.Id, manifest.Title, manifest.Version, manifest.Description, [.. languages], sha256,
            manifestYaml, variablesYaml, [.. passageYamls], [.. overridePassageYamls], [.. layoutYamls],
            [.. additionalVariableYamls], restextByLocale, restextOverridesByLocale);
        await jsModule.InvokeVoidAsync("putModuleMeta", meta);

        return new InstalledModule(manifest.Id, manifest.Version, manifest.Title, manifest.Description ?? "", languages, sha256);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string moduleId)
    {
        var jsModule = await ModuleAsync();
        await jsModule.InvokeVoidAsync("clearModule", moduleId);
    }
}
