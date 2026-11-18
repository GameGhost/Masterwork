using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;

namespace Masterwork.App.Shared.Services;

/// <summary>What the player can do with one library entry right now.</summary>
public enum LibraryEntryState
{
    /// <summary>Published by a source but not installed — offer Download.</summary>
    Available,

    /// <summary>Installed, and nothing newer is published — offer Play.</summary>
    Installed,

    /// <summary>Installed, but a subscribed source publishes a higher version — offer Play and Update.</summary>
    UpdateAvailable,
}

/// <summary>
/// One module as the player sees it, wherever it came from. An entry exists if it's installed, or
/// published by a subscribed source, or both — the two are the same row, not two rows.
/// </summary>
/// <param name="Id">The module's own id, which is what makes an installed copy and a catalog entry the same entry.</param>
/// <param name="Installed">The installed copy, if there is one.</param>
/// <param name="Catalog">The catalog entry, if a subscribed source publishes it.</param>
/// <param name="Source">The source that supplied <paramref name="Catalog"/>. Null for something only installed, e.g. a manual upload.</param>
/// <param name="Snapshot">The catalog snapshot <paramref name="Catalog"/> came from, which an install needs in order to resolve dependencies and verify hashes.</param>
/// <remarks>
/// Carries no "hidden" flag of its own: hiding is per source listing and is resolved while merging,
/// so an entry that reaches here is one the player should see. Which listing won, and which are
/// hidden, is the per-source view's business.
/// </remarks>
public sealed record LibraryEntry(
    string Id,
    InstalledModule? Installed,
    CatalogEntry? Catalog,
    ContentSource? Source,
    CatalogSnapshot? Snapshot)
{
    /// <summary>Title to display — the installed copy's, falling back to what the catalog advertises.</summary>
    public string Title => Installed?.Title ?? Catalog?.Title ?? Id;

    /// <summary>The version on offer: what's installed, or what could be.</summary>
    public string Version => Installed?.Version ?? Catalog?.Version ?? "";

    /// <summary>Description to display, from whichever side has one.</summary>
    public string? Description => Installed?.Description ?? Catalog?.Description;

    /// <summary>See <see cref="LibraryEntryState"/>.</summary>
    public LibraryEntryState State =>
        Installed is null ? LibraryEntryState.Available
        : Catalog is not null && SemVer.Compare(Catalog.Version, Installed.Version) > 0 ? LibraryEntryState.UpdateAvailable
        : LibraryEntryState.Installed;

    /// <summary>Whether this can be installed or updated from a catalog right now — false unless its source is trusted.</summary>
    public bool CanInstall =>
        Catalog is not null && Snapshot is { Decision: PackageTrustDecision.Trusted };
}

/// <summary>
/// Builds the one list of modules both New Game and Manage Modules render, merging what's installed
/// with what every subscribed source publishes.
///
/// Both pages read the same list deliberately: they differ only in which entries they show (New Game
/// omits hidden ones) and which actions they offer, so a change to how entries are resolved can't
/// leave the two screens disagreeing about what exists.
/// </summary>
public sealed class ModuleLibrary(
    CatalogService catalog,
    IModuleStore moduleStore,
    IAppSettingsStore settingsStore,
    ILogger<ModuleLibrary> logger)
{
    /// <summary>A source the app is subscribed to, and what came back from it.</summary>
    /// <param name="Source">Where it's fetched from.</param>
    /// <param name="IsPrimary">The build's own source, which can't be removed and resolves first.</param>
    /// <param name="Snapshot">What was fetched, or null if it couldn't be.</param>
    public sealed record SubscribedSource(ContentSource Source, bool IsPrimary, CatalogSnapshot? Snapshot)
    {
        /// <summary>How this source is identified in stored settings — see <see cref="HiddenCatalogListing.SourceKey"/>.</summary>
        public string Key => IsPrimary ? CatalogSourceKeys.Primary : Source.CatalogUrl;

        /// <summary>The modules this source publishes, whether or not they're hidden or shadowed by an earlier source.</summary>
        public IReadOnlyList<CatalogEntry> PublishedModules =>
            Snapshot is null ? [] : [.. Snapshot.Catalog.Modules];
    }

    /// <summary>
    /// The merged library, plus every source it was built from and the listings currently hidden.
    /// The per-source view needs all three: what a source publishes, and which of those listings the
    /// player has hidden, are what its Show/Hide toggles render.
    /// </summary>
    public sealed record Result(
        IReadOnlyList<LibraryEntry> Entries,
        IReadOnlyList<SubscribedSource> Sources,
        IReadOnlyList<HiddenCatalogListing> Hidden)
    {
        /// <summary>Whether this source's listing of this module is hidden.</summary>
        public bool IsHidden(string sourceKey, string moduleId) =>
            Hidden.Any(h => h.SourceKey == sourceKey && h.ModuleId == moduleId);
    }

    /// <summary>
    /// Fetches every subscribed source (honouring the refresh policy) and merges the results with
    /// what's installed.
    /// </summary>
    /// <param name="primary">The build's own source — on the web head this is its own origin's routes, not an upstream URL.</param>
    public async Task<Result> LoadAsync(
        ContentSource primary,
        CatalogRefreshTrigger trigger = CatalogRefreshTrigger.ReturnToMenu,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync();
        var installed = await moduleStore.ListAsync();

        var sources = new List<SubscribedSource> { new(primary, IsPrimary: true, await TryLoadAsync(primary, trigger, cancellationToken)) };
        foreach (var url in settings.CustomCatalogSources)
        {
            var source = ContentSource.ForAddedSource(url);
            sources.Add(new SubscribedSource(source, IsPrimary: false, await TryLoadAsync(source, trigger, cancellationToken)));
        }

        return new Result(Merge(sources, installed, settings.HiddenCatalogListings), sources, settings.HiddenCatalogListings);
    }

    private async Task<CatalogSnapshot?> TryLoadAsync(
        ContentSource source, CatalogRefreshTrigger trigger, CancellationToken cancellationToken)
    {
        try
        {
            return await catalog.RefreshIfDueAsync(source, trigger, cancellationToken);
        }
        catch (Exception ex) when (ex is ContentDownloadException or CatalogParseException)
        {
            // One unreachable or malformed source must not take the library down with it — the rest
            // of the player's content is still perfectly usable.
            logger.LogWarning(ex, "Source {Url} could not be loaded; continuing without it", source.CatalogUrl);
            return null;
        }
    }

    private static List<LibraryEntry> Merge(
        IReadOnlyList<SubscribedSource> sources,
        IReadOnlyList<InstalledModule> installed,
        IReadOnlyList<HiddenCatalogListing> hidden)
    {
        // Sources resolve in subscription order with the primary first, and the first one to publish
        // an id that isn't hidden keeps it. Two rules are doing work here:
        //
        // Precedence by order rather than by version number, because "highest version wins" would
        // let any added source shadow official content just by publishing a bigger number, and added
        // sources aren't pinned to the build's trust anchor.
        //
        // And hiding skips a source's listing rather than removing the module, so hiding the
        // earlier source's listing promotes the next source's — that's what lets a player move one
        // module onto a pre-release source while everything else stays on the stable one.
        var resolved = new Dictionary<string, Resolution>(StringComparer.Ordinal);

        for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
        {
            var source = sources[sourceIndex];
            if (source.Snapshot is not { } snapshot)
            {
                continue;
            }

            // Asset packs are filtered out here rather than in the pages: they're listed so a
            // module's dependency can be resolved, never browsed or installed on their own.
            var position = 0;
            foreach (var entry in snapshot.Catalog.Modules)
            {
                // Counted before the skip checks, so an entry keeps the position its catalog gave it
                // however many earlier ones were hidden or claimed by another source.
                var positionInCatalog = position++;

                if (resolved.ContainsKey(entry.Id)
                    || hidden.Any(h => h.SourceKey == source.Key && h.ModuleId == entry.Id))
                {
                    continue;
                }

                resolved[entry.Id] = new Resolution(entry, source.Source, snapshot, sourceIndex, positionInCatalog);
            }
        }

        var ranked = new List<(LibraryEntry Entry, int SourceIndex, int Position)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // An installed module is always listed, whatever is hidden — hiding a listing decides where
        // updates come from, not whether something the player already has can be played.
        foreach (var module in installed)
        {
            if (resolved.TryGetValue(module.ModuleId, out var published))
            {
                ranked.Add((new LibraryEntry(module.ModuleId, module, published.Entry, published.Source, published.Snapshot),
                    published.SourceIndex, published.Position));
            }
            else
            {
                // Installed but published by no subscribed source — a manual upload, or something
                // whose every listing is hidden. Nothing ranks it, so it sorts after everything that
                // a source does.
                ranked.Add((new LibraryEntry(module.ModuleId, module, null, null, null), int.MaxValue, 0));
            }

            seen.Add(module.ModuleId);
        }

        // Anything left is available but not installed. If every source's listing of it is hidden it
        // resolved to nothing above, so it simply isn't here — which is what hiding is for.
        foreach (var (id, published) in resolved.Where(p => !seen.Contains(p.Key)))
        {
            ranked.Add((new LibraryEntry(id, null, published.Entry, published.Source, published.Snapshot),
                published.SourceIndex, published.Position));
        }

        // Source order, then the order that source's catalog lists them in — the same precedence
        // that decided which listing won, so the list reads the way the sources are arranged and a
        // publisher controls where their own modules appear. Title only breaks ties among entries
        // no source ranks.
        return
        [
            .. ranked
                .OrderBy(r => r.SourceIndex)
                .ThenBy(r => r.Position)
                .ThenBy(r => r.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => r.Entry),
        ];
    }

    private readonly record struct Resolution(
        CatalogEntry Entry, ContentSource Source, CatalogSnapshot Snapshot, int SourceIndex, int Position);
}
