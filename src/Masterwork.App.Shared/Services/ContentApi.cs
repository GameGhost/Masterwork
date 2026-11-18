namespace Masterwork.App.Shared.Services;

/// <summary>
/// The content API a flavor's site serves, which every head uses for its primary source. The web
/// head calls it on its own origin; a native build compiles in the flavor site's address.
///
/// Under the base path a site answers <c>catalog.json</c>, <c>catalog.json.sig</c>,
/// <c>thumbnails/{path}</c> and <c>packages/{path}</c>, either by serving the bytes or by
/// redirecting to wherever they're stored, so the storage can move without any head changing.
/// </summary>
public static class ContentApi
{
    /// <summary>Path every content route sits under, relative to the site root.</summary>
    public const string BasePath = "/api/content";

    /// <summary>The primary source served by the site at <paramref name="siteUrl"/>, e.g. <c>https://example.com</c>.</summary>
    public static ContentSource ForSite(string siteUrl)
    {
        var root = siteUrl.TrimEnd('/') + BasePath;
        return new ContentSource(root + "/catalog.json", root + "/packages");
    }
}
