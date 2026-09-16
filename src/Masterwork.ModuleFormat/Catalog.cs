namespace Masterwork.ModuleFormat;

/// <summary>Player-count range for a catalog entry, mirroring <see cref="ModuleInfo"/>'s own fields.</summary>
public sealed record CatalogPlayers(int? Min, int? Max);

/// <summary>
/// A browse thumbnail: where to fetch it, and what it should hash to.
/// </summary>
/// <param name="Path">
/// Path relative to the *catalog's own directory* (e.g. <c>thumbnails/renegade.cost_of_disease.png</c>)
/// — not to the package base, since a thumbnail is published alongside the catalog rather than inside
/// a release.
/// </param>
/// <param name="Hash">
/// SHA-256 of the image bytes, hex. Lets a client keep a thumbnail across catalog refreshes and
/// re-download only when the art actually changed — the path alone can't tell it that, since a
/// thumbnail keeps its name when its content is updated.
/// </param>
public sealed record CatalogThumbnail(string Path, string Hash);

/// <summary>
/// One downloadable item in a <see cref="CatalogDocument"/>. Browse metadata is duplicated from the
/// package's own <c>manifest.yaml</c> so the app can render a full browse UI without downloading
/// anything — which means it can also drift from the package if a catalog is published by hand
/// rather than generated from the packages it lists.
/// </summary>
public sealed record CatalogEntry
{
    /// <summary><c>"module"</c> or <c>"assets"</c> — the same vocabulary <see cref="ModuleManifest.ModuleType"/> uses.</summary>
    public required string Type { get; init; }

    /// <summary>The package's own <see cref="ModuleManifest.Id"/>.</summary>
    public required string Id { get; init; }

    /// <summary>Display title. Already locale-resolved by whatever generated the catalog — a catalog carries one language's text, not a localized list.</summary>
    public required string Title { get; init; }

    /// <summary>Semver-ish version of the package this entry points at.</summary>
    public required string Version { get; init; }

    /// <summary>
    /// Where the package sits relative to its source's content base — e.g.
    /// <c>v0.4.0/cost-of-disease.mwm</c>. Deliberately not an absolute URL: a catalog names no host
    /// at all, so a tampered catalog cannot reroute a download somewhere else, and each head
    /// resolves this against a base it compiled in.
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// SHA-256 of the package bytes, hex. This is what makes a catalog-sourced install safe without
    /// the package carrying its own signature: the catalog is signed, so an entry's hash is
    /// trustworthy, and a download that doesn't match it is rejected outright.
    /// </summary>
    public required string Sha256 { get; init; }

    /// <summary>Package size in bytes — shown before downloading, and a cheap early mismatch check.</summary>
    public long Size { get; init; }

    /// <summary>Human-readable description.</summary>
    public string? Description { get; init; }

    /// <summary>Content languages the package ships (<see cref="ModuleManifest.Languages"/>).</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Player-count range, if the package declares one. Always absent for an asset pack.</summary>
    public CatalogPlayers? Players { get; init; }

    /// <summary>Human-readable playtime estimate, if declared.</summary>
    public string? Playtime { get; init; }

    /// <summary>
    /// Browse thumbnail, if this entry has one. Distinct from <see cref="ModuleThumbnail.Image"/>,
    /// which is an <c>image://</c> URI resolvable only *inside* an already-downloaded package;
    /// browsing needs something fetchable before that.
    ///
    /// Absent for an asset pack: packs are never browsed or installed directly, only pulled in as a
    /// module's dependency, so nothing would ever display one.
    /// </summary>
    public CatalogThumbnail? Thumbnail { get; init; }

    /// <summary>Asset packs this entry needs installed, by id and exact version — the same declarations the package's own manifest carries.</summary>
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = [];
}

/// <summary>
/// A content source's published index: everything it offers, with enough metadata to browse without
/// downloading. Published as a plain <c>catalog.json</c> next to a detached <c>catalog.json.sig</c>
/// (see <see cref="DetachedSignature"/>) — the signature is what makes every entry's
/// <see cref="CatalogEntry.Sha256"/> trustworthy, and therefore what lets a catalog-sourced install
/// be a hash check rather than a per-package signature check.
/// </summary>
public sealed record CatalogDocument
{
    /// <summary>Catalog format version. Only <see cref="CatalogFormatVersion.Current"/> is understood.</summary>
    public required string Format { get; init; }

    /// <summary>Display name of the source, shown wherever a player manages subscribed sources.</summary>
    public required string Title { get; init; }

    /// <summary>When this catalog was generated, UTC.</summary>
    public DateTimeOffset? Updated { get; init; }

    /// <summary>Everything this source publishes — modules and asset packs together, told apart by <see cref="CatalogEntry.Type"/>.</summary>
    public IReadOnlyList<CatalogEntry> Entries { get; init; } = [];
}

/// <summary>Rules for <see cref="CatalogEntry.Path"/>, shared by the parser and whatever resolves a path against a base.</summary>
public static class CatalogPaths
{
    /// <summary>
    /// Whether a path can be appended to a content base without escaping it. Rejects absolute URLs,
    /// rooted paths, backslashes, empty segments, and any <c>..</c> segment — an entry must not be
    /// able to reach outside the location its source publishes from, whether that's a compiled-in
    /// base or another site's catalog directory.
    /// </summary>
    public static bool IsSafeRelative(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith('/')
        && !path.Contains('\\')
        && !path.Contains("//")
        && !path.Contains(':')
        && !path.Split('/').Contains("..");
}

/// <summary>The catalog format version this build writes and understands.</summary>
public static class CatalogFormatVersion
{
    /// <summary>Current value of a catalog's <c>format:</c> field.</summary>
    public const string Current = "mwcatalog/1";
}
