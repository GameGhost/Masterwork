using System.IO.Compression;
using System.Text;

namespace Masterwork.ModuleFormat;

/// <summary>
/// A <c>.mwm</c> package's raw contents, read directly from zip bytes — no filesystem access
/// needed, so this works identically on a browser upload (<c>byte[]</c> from an <c>&lt;InputFile&gt;</c>)
/// and a MAUI <c>FilePicker</c> read. Resolve a locale with <see cref="ModuleLocales.SelectLocale"/>
/// against <see cref="RestextByLocale"/>, then feed <see cref="PassageYamls"/>/<see cref="VariablesYaml"/>,
/// <see cref="OverridePassageYamls"/>, the chosen <see cref="RestextByLocale"/> entry, and (if present)
/// the matching <see cref="RestextOverridesByLocale"/> entry straight into
/// <see cref="IModuleLoader.LoadFromSources"/>.
/// </summary>
public sealed record ModulePackageContents(
    string? ManifestYaml,
    string? VariablesYaml,
    IReadOnlyDictionary<string, string> RestextByLocale,
    IReadOnlyList<string> PassageYamls,
    IReadOnlyDictionary<string, byte[]> Assets,
    IReadOnlyList<string> OverridePassageYamls,
    IReadOnlyDictionary<string, string> RestextOverridesByLocale,
    IReadOnlyList<string> LayoutYamls,
    IReadOnlyList<string> AdditionalVariableYamls
);

/// <summary>What kind of <c>.mwm</c> entry a <see cref="ModulePackageEntry"/> represents — see <see cref="ModulePackage.ExtractEntriesAsync"/>.</summary>
public enum ModulePackageEntryKind
{
    Manifest,
    Variables,
    Restext,
    RestextOverride,
    Passage,
    PassageOverride,
    Layout,
    AdditionalVariable,
    Asset,
}

/// <summary>
/// One classified, decompressed entry from a <c>.mwm</c> zip, as streamed by
/// <see cref="ModulePackage.ExtractEntriesAsync"/>. <see cref="Path"/> is the full in-zip path
/// (e.g. <c>"assets/icons/village.png"</c>); <see cref="Locale"/> is set only for
/// <see cref="ModulePackageEntryKind.Restext"/>/<see cref="ModulePackageEntryKind.RestextOverride"/>
/// (the culture name parsed from the filename, e.g. <c>"en-US"</c>).
/// </summary>
public readonly record struct ModulePackageEntry(ModulePackageEntryKind Kind, string Path, string? Locale, byte[] Bytes);

/// <summary>
/// Reads/writes the <c>.mwm</c> zip format. Layout mirrors what the extractor and
/// <c>Masterwork-Modules/&lt;id&gt;</c> module directories already produce: extractor-owned
/// passages under <c>passages/</c> (or flat at the root, for older packages built before that split),
/// hand-authored replacements/additions under <c>passages-override/</c>, module-authored layout
/// chrome under <c>layouts/</c>, <c>_variables.yaml</c> and <c>manifest.yaml</c> at the root, and an
/// <c>assets/</c> folder for whatever an asset pack contributes (Milestone C). Any root-level
/// <c>{locale}.restext</c> file is picked up — a module
/// can ship as many as it has translations for (<c>en-US.restext</c>, <c>es.restext</c>, ...) — and a
/// sibling <c>{locale}.overrides.restext</c>, if present, is exposed separately in
/// <see cref="ModulePackageContents.RestextOverridesByLocale"/> for the same add/override-by-key
/// merge <see cref="IModuleLoader.LoadFromSources"/> applies to passage overrides.
/// </summary>
public static class ModulePackage
{
    /// <summary>Reads a <c>.mwm</c> package's contents from raw zip bytes.</summary>
    public static ModulePackageContents ReadFromBytes(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        string? manifestYaml = null;
        string? variablesYaml = null;
        var restextByLocale = new Dictionary<string, string>();
        var restextOverridesByLocale = new Dictionary<string, string>();
        var passageYamls = new List<string>();
        var overridePassageYamls = new List<string>();
        var assets = new Dictionary<string, byte[]>();
        var layoutYamls = new List<string>();
        var additionalVariableYamls = new List<string>();

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
                case ModulePackageEntryKind.Manifest:
                    manifestYaml = ReadText(entry);
                    break;
                case ModulePackageEntryKind.Variables:
                    variablesYaml = ReadText(entry);
                    break;
                case ModulePackageEntryKind.RestextOverride:
                    restextOverridesByLocale[c.Locale!] = ReadText(entry);
                    break;
                case ModulePackageEntryKind.Restext:
                    restextByLocale[c.Locale!] = ReadText(entry);
                    break;
                case ModulePackageEntryKind.Passage:
                    passageYamls.Add(ReadText(entry));
                    break;
                case ModulePackageEntryKind.PassageOverride:
                    overridePassageYamls.Add(ReadText(entry));
                    break;
                case ModulePackageEntryKind.Layout:
                    layoutYamls.Add(ReadText(entry));
                    break;
                case ModulePackageEntryKind.AdditionalVariable:
                    additionalVariableYamls.Add(ReadText(entry));
                    break;
                case ModulePackageEntryKind.Asset:
                    assets[path] = ReadBytes(entry);
                    break;
            }
        }

        return new ModulePackageContents(
            manifestYaml, variablesYaml, restextByLocale, passageYamls, assets, overridePassageYamls,
            restextOverridesByLocale, layoutYamls, additionalVariableYamls);
    }

    /// <summary>
    /// Reads only <c>manifest.yaml</c> from zip bytes, via <see cref="ZipArchive.GetEntry"/> rather
    /// than iterating/decompressing every entry — safe to call even against a 60+MB package with a
    /// large <c>assets/</c> folder, since nothing outside <c>manifest.yaml</c> itself is ever touched.
    /// Used wherever only the manifest is needed (an upload's pre-install conflict check) instead of
    /// the full <see cref="ReadFromBytes"/>.
    /// </summary>
    public static string? ReadManifestOnly(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("manifest.yaml");
        return entry is null ? null : ReadText(entry);
    }

    /// <summary>
    /// Streams every classified entry in a <c>.mwm</c> zip to <paramref name="onEntryAsync"/> one at
    /// a time, yielding periodically (not per-entry — real overhead against packages with many small
    /// files) so the caller never has to hold the whole decompressed package in memory at once, and
    /// so a single-threaded UI (WASM) stays responsive during a large install. <paramref name="progress"/>,
    /// if given, reports (entries done, total entries) — the total is known upfront
    /// (<see cref="ZipArchive.Entries"/>'s count), so this is a genuine "N of M" count, not an estimate.
    /// Unclassifiable entries (directory entries, anything <see cref="Classify"/> doesn't recognize)
    /// are skipped and not counted in "done" but are counted in "total" (so the bar still reaches 100%).
    /// </summary>
    public static async Task ExtractEntriesAsync(
        byte[] zipBytes, Func<ModulePackageEntry, ValueTask> onEntryAsync, IProgress<(int Done, int Total)>? progress = null)
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
                    var bytes = c.Kind == ModulePackageEntryKind.Asset ? ReadBytes(entry) : Encoding.UTF8.GetBytes(ReadText(entry));
                    await onEntryAsync(new ModulePackageEntry(c.Kind, path, c.Locale, bytes));
                }
            }

            done++;
            progress?.Report((done, total));

            if (done % yieldEveryNEntries == 0)
            {
                // Task.Delay(1), not Task.Yield() — see ModuleHasher's remarks on why a bare Yield
                // can resolve as a microtask on WASM without ever handing control back to the
                // browser's event loop, starving input/paint even though the awaited task "yielded".
                await Task.Delay(1);
            }
        }
    }

    /// <summary>Zips an extractor-output-shaped directory (passages + <c>_variables.yaml</c> + one or more <c>{locale}.restext</c> files at its root, plus <c>manifest.yaml</c> and an optional <c>assets/</c> folder) into <c>.mwm</c> bytes. Excludes <c>.source/</c> (a module's own copy of the CC BY-NC-SA Cradle source it was extracted from — never distributable), a root <c>README.md</c>, and a root <c>VIEW-REQUIREMENTS.md</c> — none belong in a distributable bundle.</summary>
    public static byte[] WriteToBytes(string sourceDirectory)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                var relativeName = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
                if (IsExcludedFromPackage(relativeName))
                {
                    continue;
                }

                archive.CreateEntryFromFile(file, relativeName);
            }
        }

        return stream.ToArray();
    }

    private static bool IsExcludedFromPackage(string relativeName)
    {
        var separatorIndex = relativeName.IndexOf('/');
        var firstSegment = separatorIndex < 0 ? relativeName : relativeName[..separatorIndex];
        return firstSegment.Equals(".source", StringComparison.OrdinalIgnoreCase) ||
               relativeName.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
               relativeName.Equals("VIEW-REQUIREMENTS.md", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ClassifiedPath(ModulePackageEntryKind Kind, string? Locale);

    // Single source of truth for "what is this in-zip path" — shared by ReadFromBytes and
    // ExtractEntriesAsync so the two can't drift on which paths mean what.
    private static ClassifiedPath? Classify(string path)
    {
        if (path.Equals("manifest.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.Manifest, null);
        }

        if (path.Equals("_variables.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.Variables, null);
        }

        if (!path.Contains('/') && path.EndsWith(".overrides.restext", StringComparison.OrdinalIgnoreCase))
        {
            // Checked before the plain ".restext" branch below, since this also ends with it.
            return new ClassifiedPath(ModulePackageEntryKind.RestextOverride, path[..^".overrides.restext".Length]);
        }

        if (!path.Contains('/') && path.EndsWith(".restext", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.Restext, path[..^".restext".Length]);
        }

        if (!path.Contains('/') && path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
        {
            // Legacy flat layout: passages directly at the zip root.
            return new ClassifiedPath(ModulePackageEntryKind.Passage, null);
        }

        if (path.StartsWith("passages/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.Passage, null);
        }

        if (path.StartsWith("passages-override/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.PassageOverride, null);
        }

        if (path.StartsWith("layouts/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.Layout, null);
        }

        if (path.StartsWith("variables/", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.AdditionalVariable, null);
        }

        if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedPath(ModulePackageEntryKind.Asset, null);
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
