using Masterwork.ModuleFormat;

namespace Masterwork.App.Web.Api;

/// <summary>
/// Where the static site's content API sends each request. The site stores its content under
/// <see cref="StorageBase"/>; the API exists so that storage can move (to blob storage, say) by
/// changing only this class, while every installed app keeps asking for the same addresses.
/// </summary>
/// <remarks>
/// Only the <c>ContentApi</c> routes are answered, and their paths must pass
/// <see cref="CatalogPaths.IsSafeRelative"/>. Nothing in the request chooses the destination beyond
/// that path, so this can't be used as an open redirect.
/// </remarks>
public static class ContentRedirect
{
    /// <summary>Where the site stores its content, beside the app.</summary>
    public const string StorageBase = "/content/";

    private static readonly string[] Directories = ["thumbnails/", "packages/"];

    /// <summary>
    /// The redirect target for a path under <c>/api/content/</c>, or <see langword="null"/> when the
    /// path isn't one the content API serves.
    /// </summary>
    public static string? Target(string? path)
    {
        if (path is "catalog.json" or "catalog.json.sig")
        {
            return StorageBase + path;
        }

        foreach (var directory in Directories)
        {
            if (path is not null && path.StartsWith(directory, StringComparison.Ordinal) && CatalogPaths.IsSafeRelative(path[directory.Length..]))
            {
                // Re-escaped per segment: the route value arrives decoded, and a Location header must
                // not carry a raw '?', '#' or space into the address.
                return StorageBase + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
            }
        }

        return null;
    }
}
