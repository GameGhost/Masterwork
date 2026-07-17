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

            if (path.Equals("manifest.yaml", StringComparison.OrdinalIgnoreCase))
            {
                manifestYaml = ReadText(entry);
            }
            else if (path.Equals("_variables.yaml", StringComparison.OrdinalIgnoreCase))
            {
                variablesYaml = ReadText(entry);
            }
            else if (!path.Contains('/') && path.EndsWith(".overrides.restext", StringComparison.OrdinalIgnoreCase))
            {
                // Checked before the plain ".restext" branch below, since this also ends with it.
                var locale = path[..^".overrides.restext".Length];
                restextOverridesByLocale[locale] = ReadText(entry);
            }
            else if (!path.Contains('/') && path.EndsWith(".restext", StringComparison.OrdinalIgnoreCase))
            {
                var locale = path[..^".restext".Length];
                restextByLocale[locale] = ReadText(entry);
            }
            else if (!path.Contains('/') && path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy flat layout: passages directly at the zip root.
                passageYamls.Add(ReadText(entry));
            }
            else if (path.StartsWith("passages/", StringComparison.OrdinalIgnoreCase) &&
                     path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
            {
                passageYamls.Add(ReadText(entry));
            }
            else if (path.StartsWith("passages-override/", StringComparison.OrdinalIgnoreCase) &&
                     path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
            {
                overridePassageYamls.Add(ReadText(entry));
            }
            else if (path.StartsWith("layouts/", StringComparison.OrdinalIgnoreCase) &&
                     path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
            {
                layoutYamls.Add(ReadText(entry));
            }
            else if (path.StartsWith("variables/", StringComparison.OrdinalIgnoreCase) &&
                     path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                additionalVariableYamls.Add(ReadText(entry));
            }
            else if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                assets[path] = ReadBytes(entry);
            }
        }

        return new ModulePackageContents(
            manifestYaml, variablesYaml, restextByLocale, passageYamls, assets, overridePassageYamls,
            restextOverridesByLocale, layoutYamls, additionalVariableYamls);
    }

    /// <summary>Zips an extractor-output-shaped directory (passages + <c>_variables.yaml</c> + one or more <c>{locale}.restext</c> files at its root, plus <c>manifest.yaml</c> and an optional <c>assets/</c> folder) into <c>.mwm</c> bytes. Excludes <c>.source/</c> (a module's own copy of the CC BY-NC-SA Cradle source it was extracted from — never distributable) and a root <c>README.md</c> — neither belongs in a distributable bundle.</summary>
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
               relativeName.Equals("README.md", StringComparison.OrdinalIgnoreCase);
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
