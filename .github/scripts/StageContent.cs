#:project ../../src/Masterwork.ModuleFormat/Masterwork.ModuleFormat.csproj

// Stages a flavor's content into the static web app, under the folder its content API redirects to.
//
//   dotnet run .github/scripts/StageContent.cs -- <catalog dir> <package base URL> <output dir>
//
// Copies catalog.json and catalog.json.sig, the thumbnails the catalog lists, and downloads every
// package it lists from <package base URL>/<entry path>. Each thumbnail and package must match the
// catalog's SHA-256 (and a package its size), so a deploy never publishes bytes the signed catalog
// doesn't vouch for. The catalog is read by the app's own parser, which rejects unsafe paths.

using System.Security.Cryptography;
using Masterwork.ModuleFormat;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: dotnet run StageContent.cs -- <catalog dir> <package base URL> <output dir>");
    return 2;
}

var (catalogDirectory, packageBase, outputDirectory) = (args[0], args[1].TrimEnd('/'), args[2]);

var catalogBytes = await File.ReadAllBytesAsync(Path.Combine(catalogDirectory, "catalog.json"));
var catalog = CatalogParser.Parse(catalogBytes);

Directory.CreateDirectory(outputDirectory);
foreach (var file in new[] { "catalog.json", "catalog.json.sig" })
{
    File.Copy(Path.Combine(catalogDirectory, file), Path.Combine(outputDirectory, file), overwrite: true);
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
long total = 0;

foreach (var entry in catalog.Entries)
{
    if (entry.Thumbnail is { } thumbnail)
    {
        var bytes = await File.ReadAllBytesAsync(Path.Combine(catalogDirectory, thumbnail.Path));
        await WriteVerifiedAsync(thumbnail.Path, bytes, thumbnail.Hash, expectedSize: null);
        total += bytes.Length;
    }

    var package = await http.GetByteArrayAsync($"{packageBase}/{entry.Path}");
    await WriteVerifiedAsync($"packages/{entry.Path}", package, entry.Sha256, entry.Size);
    total += package.Length;
    Console.WriteLine($"{entry.Id} {entry.Version}: {entry.Path} ({Megabytes(package.Length)})");
}

Console.WriteLine($"Staged {catalog.Entries.Count} entries, {Megabytes(total)}, into {outputDirectory}");
return 0;

async Task WriteVerifiedAsync(string relativePath, byte[] bytes, string expectedHash, long? expectedSize)
{
    if (!CatalogPaths.IsSafeRelative(relativePath))
    {
        throw new InvalidOperationException($"Unsafe path in catalog: {relativePath}");
    }
    if (expectedSize is { } size && bytes.Length != size)
    {
        throw new InvalidOperationException($"{relativePath}: {bytes.Length} bytes, the catalog says {size}");
    }
    if (!Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"{relativePath}: SHA-256 doesn't match the catalog");
    }

    var target = Path.Combine(outputDirectory, relativePath);
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    await File.WriteAllBytesAsync(target, bytes);
}

static string Megabytes(long bytes) => $"{bytes / 1048576.0:F1} MB";
