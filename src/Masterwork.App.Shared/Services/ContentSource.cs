using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Where a head fetches a catalog and the packages it lists.
///
/// Both locations come from the build, never from the catalog: entries carry relative paths only
/// (see <c>CatalogEntry.Path</c>), so a catalog has no way to name a host at all. That closes the
/// hijack route a catalog with absolute download URLs would open — tampering with a catalog could
/// then reroute downloads to an attacker's host, and would have to be caught by signature
/// verification alone rather than being impossible to express.
/// </summary>
/// <param name="CatalogUrl">Absolute URL of this source's <c>catalog.json</c>.</param>
/// <param name="PackageBaseUrl">Absolute base every entry's relative path resolves against. Trailing slash optional.</param>
public sealed record ContentSource(string CatalogUrl, string PackageBaseUrl)
{
    /// <summary>URL of the detached signature covering <see cref="CatalogUrl"/>'s exact bytes.</summary>
    public string SignatureUrl => CatalogUrl + ".sig";

    /// <summary>
    /// A source the player added themselves. Packages resolve against the catalog's own directory,
    /// so a third-party source serves its content from the same place it serves its catalog — the
    /// same-host property the primary source gets from compiled-in configuration.
    /// </summary>
    public static ContentSource ForAddedSource(string catalogUrl)
    {
        var lastSlash = catalogUrl.LastIndexOf('/');
        var directory = lastSlash < 0 ? catalogUrl : catalogUrl[..(lastSlash + 1)];
        return new ContentSource(catalogUrl, directory);
    }

    /// <summary>
    /// Resolves one entry's relative path to the URL to fetch.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="relativePath"/> isn't a safe relative path. <see cref="CatalogParser"/> rejects these at parse time too; this is the backstop for a path that reached here another way.</exception>
    public string ResolvePackageUrl(string relativePath)
    {
        if (!CatalogPaths.IsSafeRelative(relativePath))
        {
            throw new ArgumentException($"Not a safe relative content path: '{relativePath}'", nameof(relativePath));
        }

        return PackageBaseUrl.TrimEnd('/') + "/" + relativePath;
    }
}
