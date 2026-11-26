using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.App.Shared.Services;

/// <summary>A catalog listing of a newer module version, with the trusted catalog to install it from.</summary>
public sealed record ModuleUpdate(CatalogSnapshot Snapshot, CatalogEntry Entry);

/// <summary>
/// What the module-management pages and Options do with voice packs: list the installed ones, list
/// the catalog's installable ones (from cached catalogs, with no network access), install, update
/// and remove. Reading never throws: a store or catalog that can't be read gives an empty list, so a
/// voice pack problem never stands between the player and play.
/// </summary>
public sealed class VoicePackService(
    IModuleStore moduleStore,
    IVoicePackStore voicePackStore,
    CatalogService catalog,
    CatalogInstallService installer,
    ContentSource primary,
    ILogger<VoicePackService>? logger = null)
{
    private readonly ILogger<VoicePackService> _logger = logger ?? NullLogger<VoicePackService>.Instance;

    /// <summary>Every installed voice pack, or none if they can't be read.</summary>
    public async Task<IReadOnlyList<InstalledVoicePack>> ListInstalledAsync()
    {
        try
        {
            return await voicePackStore.ListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the installed voice packs");
            return [];
        }
    }

    /// <summary>The catalog voice packs for installed modules (<see cref="VoicePackOffers.From"/>).</summary>
    public async Task<IReadOnlyList<VoicePackOffer>> LoadOffersAsync()
    {
        try
        {
            var installed = (await ListInstalledAsync()).Select(p => p.Manifest).ToList();
            return VoicePackOffers.From(await catalog.GetCachedSubscribedAsync(primary), await moduleStore.ListAsync(), installed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list the voice packs on offer");
            return [];
        }
    }

    /// <summary>Installs or updates an offered pack.</summary>
    /// <exception cref="CatalogInstallException">The download failed or the pack was refused (<see cref="VoicePackRefusedException"/>).</exception>
    public Task InstallAsync(VoicePackOffer offer, IProgress<CatalogInstallProgress>? progress = null) =>
        installer.InstallAsync(offer.Snapshot, offer.Entry, progress);

    /// <summary>
    /// The newest listing of <paramref name="moduleId"/> at <paramref name="minVersion"/> or later in
    /// the first trusted cached catalog that has one, or <see langword="null"/>.
    /// </summary>
    public async Task<ModuleUpdate?> FindModuleUpdateAsync(string moduleId, string minVersion)
    {
        try
        {
            foreach (var snapshot in (await catalog.GetCachedSubscribedAsync(primary)).Where(s => s.Decision == PackageTrustDecision.Trusted))
            {
                if (VoicePackOffers.NewestModuleListing(snapshot.Catalog, moduleId, minVersion) is { } entry)
                {
                    return new ModuleUpdate(snapshot, entry);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look for an update of {ModuleId}", moduleId);
        }

        return null;
    }

    /// <summary>Installs a module update found by <see cref="FindModuleUpdateAsync"/>, or an offer's <see cref="VoicePackOffer.ModuleUpdate"/>.</summary>
    /// <exception cref="CatalogInstallException">The update failed.</exception>
    public Task UpdateModuleAsync(ModuleUpdate update, IProgress<CatalogInstallProgress>? progress = null) =>
        installer.InstallAsync(update.Snapshot, update.Entry, progress);

    /// <summary>
    /// Why the uploaded pack <paramref name="manifest"/> can't be installed, or <see langword="null"/>
    /// if it can, checked against the installed module and packs (<see cref="VoicePackInstallRules"/>).
    /// </summary>
    public async Task<VoicePackRefusal?> CheckAsync(VoicePackManifest manifest)
    {
        var module = (await moduleStore.ListAsync()).FirstOrDefault(m => m.ModuleId == manifest.For.ModuleId);
        var installed = (await voicePackStore.ListAsync()).Select(p => p.Manifest).ToList();
        return VoicePackInstallRules.Check(manifest, module, installed);
    }

    /// <summary>Installs an uploaded pack that <see cref="CheckAsync"/> accepted.</summary>
    public Task<InstalledVoicePack> InstallUploadAsync(byte[] packageBytes, IProgress<(int Done, int Total)>? progress = null) =>
        voicePackStore.InstallAsync(packageBytes, progress);

    /// <summary>
    /// Installs the voice pack an uploaded <paramref name="module"/> needs for the player's voice
    /// language, from the cached catalogs, as a catalog install does. Never throws.
    /// </summary>
    public async Task InstallForModuleAsync(InstalledModule module, IProgress<CatalogInstallProgress>? progress = null)
    {
        try
        {
            await installer.InstallVoicePackForModuleAsync(await catalog.GetCachedSubscribedAsync(primary), module, progress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No voice pack installed with uploaded module {ModuleId}", module.ModuleId);
        }
    }

    /// <summary>The size of a download, for display.</summary>
    public static string FormatSize(long bytes) =>
        string.Format(Resources.AppStrings.EffectiveCulture, Resources.AppStrings.Common_SizeMegabytes, bytes / (1024.0 * 1024.0));

    /// <summary>Removes one installed pack.</summary>
    public Task DeleteAsync(string packId) => voicePackStore.DeleteAsync(packId);

    /// <summary>Removes every installed pack for <paramref name="moduleId"/>.</summary>
    public async Task DeleteForModuleAsync(string moduleId)
    {
        foreach (var pack in await voicePackStore.ListForModuleAsync(moduleId))
        {
            await voicePackStore.DeleteAsync(pack.Manifest.Id);
        }
    }
}
