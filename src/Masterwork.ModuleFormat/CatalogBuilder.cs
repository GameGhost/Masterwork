using System.IO.Compression;
using System.Security.Cryptography;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Builds a <see cref="CatalogDocument"/> from the packages that will actually be published.
///
/// Every entry's browse metadata is read out of the package's own <c>manifest.yaml</c> rather than
/// being written by hand, and its hash and size are measured from the exact bytes being uploaded —
/// so a catalog can't drift from what it describes, and a hash can't be stale.
/// </summary>
public static class CatalogBuilder
{
    /// <summary>
    /// Describes one package to include: the bytes to be published, and the relative path they will
    /// live at under the source's content base (e.g. <c>v0.4.0/cost-of-disease.mwm</c>).
    /// </summary>
    public sealed record PackageInput(string RelativePath, byte[] Bytes);

    /// <summary>
    /// A browse thumbnail lifted out of a module package, to be published beside the catalog at
    /// <see cref="RelativePath"/> (the same value the entry's <see cref="CatalogEntry.Thumbnail"/>
    /// carries).
    /// </summary>
    public sealed record ThumbnailOutput(string RelativePath, byte[] Bytes);

    /// <summary>The catalog, plus the thumbnail files that have to be published alongside it for its entries to render.</summary>
    public sealed record Result(CatalogDocument Catalog, IReadOnlyList<ThumbnailOutput> Thumbnails);

    // Same "assets/images/{slug}{ext}" probing order ModuleThumbnailResolver uses at runtime, so a
    // thumbnail that displays in the app is the one that ends up in the catalog.
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>
    /// Reads each package's manifest and produces the catalog describing all of them, along with any
    /// thumbnails extracted from module packages.
    /// </summary>
    /// <param name="title">Display name of the source.</param>
    /// <param name="packages">Packages to list, in the order they should appear.</param>
    /// <param name="updated">Generation timestamp; defaults to now.</param>
    /// <exception cref="ArgumentException">A package has no manifest, or its path isn't a safe relative path.</exception>
    public static Result Build(
        string title,
        IEnumerable<PackageInput> packages,
        DateTimeOffset? updated = null)
    {
        var entries = new List<CatalogEntry>();
        var thumbnails = new List<ThumbnailOutput>();

        foreach (var package in packages)
        {
            if (!CatalogPaths.IsSafeRelative(package.RelativePath))
            {
                throw new ArgumentException(
                    $"'{package.RelativePath}' isn't a safe relative content path.", nameof(packages));
            }

            var manifestYaml = ModulePackage.ReadManifestOnly(package.Bytes)
                ?? throw new ArgumentException($"'{package.RelativePath}' has no manifest.yaml.", nameof(packages));

            entries.Add(BuildEntry(package, manifestYaml, thumbnails));
        }

        return new Result(
            new CatalogDocument
            {
                Format = CatalogFormatVersion.Current,
                Title = title,
                Updated = updated ?? DateTimeOffset.UtcNow,
                Entries = entries,
            },
            thumbnails);
    }

    private static CatalogEntry BuildEntry(PackageInput package, string manifestYaml, List<ThumbnailOutput> thumbnails)
    {
        // Parsed as a module manifest for both package kinds: an asset-pack manifest has none of the
        // module-only fields, so this reads cleanly and still yields the identity and display fields
        // a catalog entry needs. `type:` is what says which kind it actually is.
        var manifest = new ManifestParser().Parse(manifestYaml);
        var isAssetPack = manifest.ModuleType == "assets";

        // Asset packs get no thumbnail: they're never browsed or installed directly, only pulled in
        // as a module's dependency, so nothing would ever display one.
        CatalogThumbnail? thumbnail = null;
        if (!isAssetPack && ExtractThumbnail(package.Bytes, manifest.Thumbnail?.Image) is var (extension, bytes))
        {
            thumbnail = new CatalogThumbnail(
                $"thumbnails/{manifest.Id}{extension}",
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            thumbnails.Add(new ThumbnailOutput(thumbnail.Path, bytes));
        }

        return new CatalogEntry
        {
            Type = isAssetPack ? "assets" : "module",
            Id = manifest.Id,
            Title = manifest.Title,
            Version = manifest.Version,
            Path = package.RelativePath,
            Sha256 = Convert.ToHexString(SHA256.HashData(package.Bytes)).ToLowerInvariant(),
            Size = package.Bytes.LongLength,
            Description = manifest.Description,
            Languages = manifest.Languages,

            // Asset packs have no player-facing play metadata of their own, and carry no
            // dependencies by definition — a leaf content type.
            Players = isAssetPack || manifest.Info is null
                ? null
                : new CatalogPlayers(manifest.Info.PlayersMin, manifest.Info.PlayersMax),
            Playtime = isAssetPack ? null : manifest.Info?.Playtime,
            Thumbnail = thumbnail,
            Dependencies = isAssetPack ? [] : manifest.Dependencies,
        };
    }

    // Pulls the one image the manifest names out of the package, without decompressing the rest of
    // it. Returns null when the manifest declares no thumbnail, or names one the package doesn't
    // actually contain — a catalog entry without a thumbnail still browses, just without art.
    private static (string Extension, byte[] Bytes)? ExtractThumbnail(byte[] packageBytes, string? imageUri)
    {
        const string scheme = "image://";
        if (imageUri is null || !imageUri.StartsWith(scheme, StringComparison.Ordinal))
        {
            return null;
        }

        var slug = imageUri[scheme.Length..];

        using var stream = new MemoryStream(packageBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var extension in ImageExtensions)
        {
            if (archive.GetEntry($"assets/images/{slug}{extension}") is not { } entry)
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            return (extension, buffer.ToArray());
        }

        return null;
    }
}
