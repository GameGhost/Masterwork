using System.IO.Compression;
using System.Text;

namespace Masterwork.ModuleFormat;

/// <summary>
/// A <c>.mwassets</c> package's raw contents, read directly from zip bytes — no filesystem access
/// needed, mirroring <see cref="ModulePackageContents"/>'s own contract. Resolve a locale with
/// <see cref="ModuleLocales.SelectLocale"/> against <see cref="RestextByLocale"/>, using the asset
/// pack's own <see cref="AssetPackManifest.DefaultLocale"/> — not any dependent module's — as the
/// fallback.
/// </summary>
public sealed record AssetPackPackageContents(
    string? ManifestYaml,
    string? VariablesYaml,
    IReadOnlyDictionary<string, string> RestextByLocale,
    IReadOnlyList<string> LayoutYamls,
    IReadOnlyDictionary<string, byte[]> Assets
);

/// <summary>What kind of <c>.mwassets</c> entry an <see cref="AssetPackPackageEntry"/> represents — see <see cref="AssetPackPackage.ExtractEntriesAsync"/>.</summary>
public enum AssetPackPackageEntryKind
{
    Manifest,
    Variables,
    Restext,
    Layout,
    Asset,
}

/// <summary>
/// One classified, decompressed entry from a <c>.mwassets</c> zip, as streamed by
/// <see cref="AssetPackPackage.ExtractEntriesAsync"/>. <see cref="Path"/> is the full in-zip path;
/// <see cref="Locale"/> is set only for <see cref="AssetPackPackageEntryKind.Restext"/>.
/// </summary>
public readonly record struct AssetPackPackageEntry(AssetPackPackageEntryKind Kind, string Path, string? Locale, byte[] Bytes);

/// <summary>
/// Reads/writes the <c>.mwassets</c> zip format — an asset pack never has passages, passage
/// overrides, or an additional-variables folder the way a module does, so this is a deliberately
/// narrower sibling of <see cref="ModulePackage"/> rather than sharing its implementation. Layout:
/// <c>manifest.yaml</c> and an optional <c>_variables.yaml</c> at the root (same
/// <c>standard_variables</c>/<c>variables:</c> schema <see cref="IVariableManifest"/> already parses),
/// zero or more root-level <c>{locale}.restext</c> files (no override convention — an asset pack is a
/// single canonical source, not extractor output layered with hand overrides), an optional
/// <c>layouts/</c> folder (same <c>layouts/*.mws.yaml</c> schema modules use), and an optional
/// <c>assets/</c> folder.
/// </summary>
public static class AssetPackPackage
{
    /// <summary>Reads a <c>.mwassets</c> package's contents from raw zip bytes.</summary>
    public static AssetPackPackageContents ReadFromBytes(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        string? manifestYaml = null;
        string? variablesYaml = null;
        var restextByLocale = new Dictionary<string, string>();
        var layoutYamls = new List<string>();
        var assets = new Dictionary<string, byte[]>();

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // directory entry
            }

            var path = entry.FullName.Replace('\\', '/');
            var classified = Classify(path);
            if (classified is not { } c)
            {
                continue;
            }

            switch (c.Kind)
            {
                case AssetPackPackageEntryKind.Manifest:
                    manifestYaml = ReadText(entry);
                    break;
                case AssetPackPackageEntryKind.Variables:
                    variablesYaml = ReadText(entry);
                    break;
                case AssetPackPackageEntryKind.Restext:
                    restextByLocale[c.Locale!] = ReadText(entry);
                    break;
                case AssetPackPackageEntryKind.Layout:
                    layoutYamls.Add(ReadText(entry));
                    break;
                case AssetPackPackageEntryKind.Asset:
                    assets[path] = ReadBytes(entry);
                    break;
            }
        }

        return new AssetPackPackageContents(manifestYaml, variablesYaml, restextByLocale, layoutYamls, assets);
    }

    /// <summary>
    /// Reads only <c>manifest.yaml</c> from zip bytes, via <see cref="ZipArchive.GetEntry"/> rather
    /// than iterating/decompressing every entry — same reasoning as <see cref="ModulePackage.ReadManifestOnly"/>.
    /// </summary>
    public static string? ReadManifestOnly(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("manifest.yaml");
        return entry is null ? null : ReadText(entry);
    }

    /// <summary>
    /// Streams every classified entry in a <c>.mwassets</c> zip to <paramref name="onEntryAsync"/>
    /// one at a time — same streaming/yielding contract as <see cref="ModulePackage.ExtractEntriesAsync"/>.
    /// </summary>
    public static async Task ExtractEntriesAsync(
        byte[] zipBytes, Func<AssetPackPackageEntry, ValueTask> onEntryAsync, IProgress<(int Done, int Total)>? progress = null)
    {
        const int yieldEveryNEntries = 8;

        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var total = archive.Entries.Count;
        var done = 0;
        progress?.Report((done, total));

        foreach (var entry in archive.Entries)
        {
            if (!string.IsNullOrEmpty(entry.Name))
            {
                var path = entry.FullName.Replace('\\', '/');
                if (Classify(path) is { } c)
                {
                    var bytes = c.Kind == AssetPackPackageEntryKind.Asset ? ReadBytes(entry) : Encoding.UTF8.GetBytes(ReadText(entry));
                    await onEntryAsync(new AssetPackPackageEntry(c.Kind, path, c.Locale, bytes));
                }
            }

            done++;
            progress?.Report((done, total));

            if (done % yieldEveryNEntries == 0)
            {
                // See ModuleHasher's remarks (referenced from ModulePackage) on why a bare Task.Yield
                // can resolve as a microtask on WASM without handing control back to the browser's
                // event loop — Task.Delay(1) is the real yield.
                await Task.Delay(1);
            }
        }
    }

    /// <summary>Zips an asset-pack-shaped directory (<c>manifest.yaml</c>, optional <c>_variables.yaml</c>, root-level <c>{locale}.restext</c> files, optional <c>layouts/</c> and <c>assets/</c>) into <c>.mwassets</c> bytes.</summary>
    public static byte[] WriteToBytes(string sourceDirectory)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativeName = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                archive.CreateEntryFromFile(file, relativeName);
            }
        }

        return stream.ToArray();
    }

    private readonly record struct ClassifiedPath(AssetPackPackageEntryKind Kind, string? Locale);

    // Single source of truth for "what is this in-zip path" — shared by ReadFromBytes and
    // ExtractEntriesAsync so the two can't drift on which paths mean what. Mirrors
    // ModulePackage.Classify's own convention, minus everything an asset pack doesn't have.
    private static ClassifiedPath? Classify(string path)
    {
        if (path.Equals("manifest.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(AssetPackPackageEntryKind.Manifest, null);
        }

        if (path.Equals("_variables.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(AssetPackPackageEntryKind.Variables, null);
        }

        if (!path.Contains('/') && path.EndsWith(".restext", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(AssetPackPackageEntryKind.Restext, path[..^".restext".Length]);
        }

        if (path.StartsWith("layouts/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(AssetPackPackageEntryKind.Layout, null);
        }

        if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(AssetPackPackageEntryKind.Asset, null);
        }

        return null;
    }

    private static string ReadText(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var memory = new MemoryStream();
        using (var entryStream = entry.Open())
        {
            entryStream.CopyTo(memory);
        }

        return memory.ToArray();
    }
}
