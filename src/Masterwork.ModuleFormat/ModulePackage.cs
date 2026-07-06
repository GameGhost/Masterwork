using System.IO.Compression;
using System.Text;

namespace Masterwork.ModuleFormat;

/// <summary>
/// A <c>.mwm</c> package's raw contents, read directly from zip bytes — no filesystem access
/// needed, so this works identically on a browser upload (<c>byte[]</c> from an <c>&lt;InputFile&gt;</c>)
/// and a MAUI <c>FilePicker</c> read. Feed <see cref="PassageYamls"/>/<see cref="VariablesYaml"/>/
/// <see cref="RestextText"/> straight into <see cref="IModuleLoader.LoadFromSources"/>.
/// </summary>
public sealed record ModulePackageContents(
    string? ManifestYaml,
    string? VariablesYaml,
    string? RestextText,
    IReadOnlyList<string> PassageYamls,
    IReadOnlyDictionary<string, byte[]> Assets
);

/// <summary>
/// Reads/writes the <c>.mwm</c> zip format. Layout mirrors what the extractor already produces
/// (passages, <c>_variables.yaml</c>, and <c>en-US.restext</c> flat at the root — not the
/// <c>scenes/</c>/<c>i18n/</c> subfolder sketch from an earlier design pass, which never matched
/// real extractor output) plus a new root-level <c>manifest.yaml</c> and an <c>assets/</c> folder
/// for whatever an asset pack contributes (Milestone C).
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
        string? restextText = null;
        var passageYamls = new List<string>();
        var assets = new Dictionary<string, byte[]>();

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
            else if (path.Equals("en-US.restext", StringComparison.OrdinalIgnoreCase))
            {
                restextText = ReadText(entry);
            }
            else if (!path.Contains('/') && path.EndsWith(".mws.yaml", StringComparison.OrdinalIgnoreCase))
            {
                passageYamls.Add(ReadText(entry));
            }
            else if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
            {
                assets[path] = ReadBytes(entry);
            }
        }

        return new ModulePackageContents(manifestYaml, variablesYaml, restextText, passageYamls, assets);
    }

    /// <summary>Zips an extractor-output-shaped directory (passages + <c>_variables.yaml</c> + <c>en-US.restext</c> at its root, plus <c>manifest.yaml</c> and an optional <c>assets/</c> folder) into <c>.mwm</c> bytes.</summary>
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
