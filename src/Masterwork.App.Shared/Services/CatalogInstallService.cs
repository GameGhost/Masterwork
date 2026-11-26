using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>Which stage of a catalog install is running, so the UI can say more than "working".</summary>
public enum CatalogInstallPhase
{
    /// <summary>Fetching package bytes.</summary>
    Downloading,

    /// <summary>Hashing what was fetched to compare against the catalog's entry.</summary>
    Verifying,

    /// <summary>Unpacking into the installed-content store.</summary>
    Installing,
}

/// <summary>Progress for one install. <paramref name="Title"/> names the item, since one request can install a dependency first.</summary>
public readonly record struct CatalogInstallProgress(CatalogInstallPhase Phase, string Title, int Percent);

/// <summary>An install that couldn't be completed. The message is player-facing.</summary>
public class CatalogInstallException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A voice pack install refused by <see cref="VoicePackInstallRules"/>. <see cref="Refusal"/> says why, for the UI to word.</summary>
public sealed class VoicePackRefusedException(VoicePackRefusal refusal, string packTitle)
    : CatalogInstallException(refusal.Kind == VoicePackRefusalKind.NeedsModuleVersion
        ? $"'{packTitle}' needs version {refusal.ModuleVersion} or later of its module."
        : $"'{packTitle}' adds a voice track that '{refusal.InstalledBy}' already installed.")
{
    /// <summary>Why the install was refused.</summary>
    public VoicePackRefusal Refusal { get; } = refusal;
}

/// <summary>
/// Installs content named by a catalog entry.
///
/// A catalog-sourced install verifies by <b>hash</b>, not by a package signature: the catalog itself
/// is signed, so its entries' hashes are trustworthy, and one signing operation per publish covers
/// everything it lists. A package downloaded here doesn't need to carry its own signature, and a
/// hash mismatch is a hard failure — it means a corrupted or substituted download, which retrying
/// can fix and "install anyway" cannot.
/// </summary>
public sealed class CatalogInstallService(
    IContentDownloader downloader,
    IModuleStore moduleStore,
    IAssetPackStore assetPackStore,
    IVoicePackStore voicePackStore,
    IAppSettingsStore settingsStore,
    IJSRuntime js,
    BrowserCrypto crypto,
    ILogger<CatalogInstallService> logger)
{
    /// <summary>
    /// Installs one catalog entry, first installing any asset-pack dependency it declares that isn't
    /// already present. Dependencies pulled in this way are recorded as
    /// <see cref="AssetPackInstallSource.Auto"/>, so removing the last module that needed one cleans
    /// it up; an entry the player asked for directly is always <see cref="AssetPackInstallSource.Manual"/>.
    ///
    /// A module install also installs the same catalog's voice pack for the player's effective voice
    /// language, if one is needed (<see cref="VoicePackOffers.ForModuleInstall"/>). That part never
    /// fails the install: a pack that can't be fetched is logged, and narration uses what's installed.
    /// </summary>
    /// <exception cref="CatalogInstallException">The catalog isn't trusted, a dependency is unavailable, a download failed, or a hash didn't match.</exception>
    /// <exception cref="VoicePackRefusedException">A voice pack entry was refused by <see cref="VoicePackInstallRules"/>.</exception>
    public async Task InstallAsync(
        CatalogSnapshot snapshot,
        CatalogEntry entry,
        IProgress<CatalogInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Enforced here and not just in the UI: an entry's hash is only as trustworthy as the
        // signature over the catalog that carried it, so installing from an unverified catalog
        // would silently drop the guarantee this whole path depends on.
        if (snapshot.Decision != PackageTrustDecision.Trusted)
        {
            throw new CatalogInstallException("This catalog isn't from a trusted publisher, so its content can't be installed.");
        }

        if (entry.Type == VoicePackManifestParser.PackageType)
        {
            await InstallVoicePackAsync(snapshot.Source, entry, progress, cancellationToken);
            return;
        }

        foreach (var dependency in entry.Dependencies)
        {
            await EnsureDependencyAsync(snapshot, dependency, progress, cancellationToken);
        }

        var module = await InstallEntryAsync(
            snapshot.Source,
            entry,
            entry.Type == "assets" ? AssetPackInstallSource.Manual : null,
            progress,
            cancellationToken);

        if (module is not null)
        {
            await InstallVoicePackForModuleAsync([snapshot], module, progress, cancellationToken);
        }
    }

    /// <summary>
    /// Installs the voice pack <paramref name="module"/> needs for the player's effective voice
    /// language (<see cref="VoicePackOffers.ForModuleInstall"/>), from the first trusted
    /// <paramref name="snapshots"/> listing one. Never throws except on cancellation: a pack that
    /// can't be fetched is logged, and narration uses what's installed.
    /// </summary>
    public async Task InstallVoicePackForModuleAsync(
        IReadOnlyList<CatalogSnapshot> snapshots, InstalledModule module, IProgress<CatalogInstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await settingsStore.LoadAsync();
            var installed = (await voicePackStore.ListAsync()).Select(p => p.Manifest).ToList();
            var offers = VoicePackOffers.From(snapshots, [module], installed);
            if (VoicePackOffers.ForModuleInstall(settings, module, offers, installed) is not { } offer)
            {
                return;
            }

            logger.LogInformation("Catalog install: installing voice pack {PackId} v{Version} with {ModuleId}", offer.Entry.Id, offer.Entry.Version, module.ModuleId);
            await InstallVoicePackAsync(offer.Snapshot.Source, offer.Entry, progress, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Catalog install: no voice pack installed with {ModuleId}; narration uses what's already installed", module.ModuleId);
        }
    }

    private async Task InstallVoicePackAsync(
        ContentSource source, CatalogEntry entry, IProgress<CatalogInstallProgress>? progress, CancellationToken cancellationToken)
    {
        var module = (await moduleStore.ListAsync()).FirstOrDefault(m => m.ModuleId == entry.For?.ModuleId);

        // Both checks need only the catalog entry, so a pack that can't be installed isn't downloaded first.
        if (module is not null && entry.For is { } target && !VoiceSelection.SupportsModuleVersion(target, module.Version))
        {
            throw new VoicePackRefusedException(new VoicePackRefusal(VoicePackRefusalKind.NeedsModuleVersion, ModuleVersion: target.MinVersion), entry.Title);
        }

        if ((await voicePackStore.ListAsync()).FirstOrDefault(p => p.Manifest.Id == entry.Id) is { } current
            && SemVer.Compare(current.Manifest.Version, entry.Version) > 0)
        {
            throw new CatalogInstallException($"'{entry.Title}' v{current.Manifest.Version} is already installed, newer than v{entry.Version}.");
        }

        var (bytes, _) = await DownloadVerifiedAsync(source, entry, progress, cancellationToken);

        VoicePackManifest manifest;
        try
        {
            manifest = VoicePackStores.ReadManifest(bytes).Manifest;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MwsParseException or YamlDotNet.Core.YamlException)
        {
            throw new CatalogInstallException($"'{entry.Title}' isn't a readable voice pack.", ex);
        }

        var installed = (await voicePackStore.ListAsync()).Select(p => p.Manifest).ToList();
        if (VoicePackInstallRules.Check(manifest, module, installed) is { } refusal)
        {
            logger.LogWarning("Catalog install: voice pack {PackId} refused ({Kind})", manifest.Id, refusal.Kind);
            throw new VoicePackRefusedException(refusal, entry.Title);
        }

        progress?.Report(new CatalogInstallProgress(CatalogInstallPhase.Installing, entry.Title, 0));
        await voicePackStore.InstallAsync(bytes, new Progress<(int Done, int Total)>(p => progress?.Report(
            new CatalogInstallProgress(CatalogInstallPhase.Installing, entry.Title, Percent(p.Done, p.Total)))));
        logger.LogInformation("Catalog install: installed voice pack {PackId} v{Version}", manifest.Id, manifest.Version);
    }

    private async Task EnsureDependencyAsync(
        CatalogSnapshot snapshot,
        ModuleDependency dependency,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        // A dependency with no pinned version can't be checked for or installed precisely; the
        // module load path already reports it as a missing_dependency warning rather than failing.
        if (dependency.Version is null)
        {
            return;
        }

        var installed = await assetPackStore.ListAsync();
        if (installed.Any(p => p.AssetPackId == dependency.Id && p.Version == dependency.Version))
        {
            return;
        }

        var dependencyEntry = snapshot.Catalog.Entries.FirstOrDefault(
            e => e.Id == dependency.Id && e.Version == dependency.Version);

        if (dependencyEntry is null)
        {
            throw new CatalogInstallException(
                $"This needs asset pack '{dependency.Id}' v{dependency.Version}, which this source doesn't publish.");
        }

        logger.LogInformation("Catalog install: auto-installing dependency {Id} v{Version}", dependency.Id, dependency.Version);
        await InstallEntryAsync(snapshot.Source, dependencyEntry, AssetPackInstallSource.Auto, progress, cancellationToken);
    }

    // Installs a module (returned) or an asset pack (null).
    private async Task<InstalledModule?> InstallEntryAsync(
        ContentSource source,
        CatalogEntry entry,
        AssetPackInstallSource? assetPackSource,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var (bytes, sha256) = await DownloadVerifiedAsync(source, entry, progress, cancellationToken);

        progress?.Report(new CatalogInstallProgress(CatalogInstallPhase.Installing, entry.Title, 0));
        var installProgress = new Progress<(int Done, int Total)>(p => progress?.Report(
            new CatalogInstallProgress(CatalogInstallPhase.Installing, entry.Title, Percent(p.Done, p.Total))));

        if (assetPackSource is { } packSource)
        {
            await assetPackStore.InstallAsync(bytes, sha256, packSource, installProgress);
            logger.LogInformation("Catalog install: installed asset pack {Id} v{Version} ({Source})", entry.Id, entry.Version, packSource);
            return null;
        }

        var module = await moduleStore.InstallAsync(bytes, sha256, installProgress);
        logger.LogInformation("Catalog install: installed module {Id} v{Version}", entry.Id, entry.Version);
        return module;
    }

    private async Task<(byte[] Bytes, string Sha256)> DownloadVerifiedAsync(
        ContentSource source,
        CatalogEntry entry,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bytes = await DownloadAsync(source, entry, progress, cancellationToken);

        progress?.Report(new CatalogInstallProgress(CatalogInstallPhase.Verifying, entry.Title, 0));
        var sha256 = await ModuleHasher.ComputeHashAsync(
            bytes,
            new Progress<(int Done, int Total)>(p => progress?.Report(
                new CatalogInstallProgress(CatalogInstallPhase.Verifying, entry.Title, Percent(p.Done, p.Total)))),
            js,
            crypto);

        if (!string.Equals(sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError(
                "Catalog install: hash mismatch for {Id} v{Version} (catalog {Expected}, download {Actual})",
                entry.Id, entry.Version, entry.Sha256, sha256);
            throw new CatalogInstallException(
                $"'{entry.Title}' didn't match what the catalog said it should be. The download may be corrupted — try again.");
        }

        return (bytes, sha256);
    }

    private async Task<byte[]> DownloadAsync(
        ContentSource source,
        CatalogEntry entry,
        IProgress<CatalogInstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new CatalogInstallProgress(CatalogInstallPhase.Downloading, entry.Title, 0));

        // The catalog's declared size is the more reliable total: a proxied or chunked response may
        // not declare a Content-Length of its own.
        var total = entry.Size > 0 ? entry.Size : (long?)null;
        var downloadProgress = new Progress<(long Done, long? Total)>(p => progress?.Report(
            new CatalogInstallProgress(CatalogInstallPhase.Downloading, entry.Title, Percent(p.Done, p.Total ?? total ?? 0))));

        try
        {
            return await downloader.GetAsync(source.ResolvePackageUrl(entry.Path), downloadProgress, cancellationToken);
        }
        catch (ContentDownloadException ex)
        {
            throw new CatalogInstallException($"Couldn't download '{entry.Title}': {ex.Message}", ex);
        }
    }

    private static int Percent(long done, long total) =>
        total <= 0 ? 0 : (int)Math.Clamp(done * 100 / total, 0, 100);
}
