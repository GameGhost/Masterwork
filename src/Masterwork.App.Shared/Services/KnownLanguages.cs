using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.App.Shared.Services;

/// <summary>The text and voice languages Options can offer, each sorted and without duplicates (ignoring case).</summary>
public sealed record KnownLanguages(IReadOnlyList<string> Text, IReadOnlyList<string> Voice)
{
    /// <summary>
    /// Text languages from installed modules and catalog modules; voice languages from installed
    /// modules' own tracks, installed voice packs and catalog voice packs.
    /// </summary>
    public static KnownLanguages From(
        IEnumerable<InstalledModule> modules, IEnumerable<InstalledVoicePack> voicePacks, IEnumerable<CatalogEntry> catalogEntries)
    {
        var moduleList = modules.ToList();
        var entryList = catalogEntries.ToList();
        return new KnownLanguages(
            Distinct(moduleList.SelectMany(m => m.AvailableLanguages)
                .Concat(entryList.Where(e => e.Type == "module").SelectMany(e => e.Languages))),
            Distinct(moduleList.SelectMany(m => m.VoiceLanguages)
                .Concat(voicePacks.SelectMany(p => p.Manifest.Voices.Select(v => v.Language)))
                .Concat(entryList.Where(e => e.Type == VoicePackManifestParser.PackageType).SelectMany(e => e.Languages))));
    }

    private static IReadOnlyList<string> Distinct(IEnumerable<string> languages) =>
        [.. languages.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// Loads <see cref="KnownLanguages"/> from installed content and the cached catalog of every
/// subscribed source, with no network access. A source or store that can't be read is skipped.
/// </summary>
public sealed class KnownLanguagesService(
    IModuleStore moduleStore,
    IInstalledVoicePacks voicePacks,
    CatalogService catalog,
    IAppSettingsStore settingsStore,
    ContentSource primary,
    ILogger<KnownLanguagesService>? logger = null)
{
    private readonly ILogger<KnownLanguagesService> _logger = logger ?? NullLogger<KnownLanguagesService>.Instance;

    /// <summary>The languages known right now.</summary>
    public async Task<KnownLanguages> LoadAsync()
    {
        var settings = await settingsStore.LoadAsync();
        var entries = new List<CatalogEntry>();
        foreach (var source in settings.CustomCatalogSources.Select(ContentSource.ForAddedSource).Prepend(primary))
        {
            try
            {
                if (await catalog.GetCachedAsync(source) is { Decision: not PackageTrustDecision.Blocked } snapshot)
                {
                    entries.AddRange(snapshot.Catalog.Entries);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the cached catalog of {Url}; its languages aren't listed", source.CatalogUrl);
            }
        }

        IReadOnlyList<InstalledVoicePack> packs;
        try
        {
            packs = await voicePacks.ListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the installed voice packs; their languages aren't listed");
            packs = [];
        }

        return KnownLanguages.From(await moduleStore.ListAsync(), packs, entries);
    }
}
