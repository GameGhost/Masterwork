using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>One passage or layout document a module directory would load, for pack-time checks.</summary>
/// <param name="Where">The file relative to the module, plus the passage id when it has one, for messages.</param>
/// <param name="Root">The document's root node.</param>
internal readonly record struct ModuleSourceDocument(string Where, YamlNode Root);

/// <summary>
/// Reads every passage and layout document in a module directory the way <see cref="ModuleLoader"/>
/// would load it: base passages replaced by a <c>passages-override/</c> passage of the same id are
/// left out.
/// </summary>
internal static class ModuleSourceDocuments
{
    public static IEnumerable<ModuleSourceDocument> Enumerate(string moduleDirectory)
    {
        var (passagesDir, overrideDir) = ModuleLoader.ResolvePassageDirs(moduleDirectory);
        var overrideFiles = PassageFiles(overrideDir).ToList();
        var overrideIds = new HashSet<string>(
            overrideFiles.SelectMany(LoadDocuments).Select(PassageId).OfType<string>(), StringComparer.Ordinal);

        var files = PassageFiles(passagesDir).Select(f => (File: f, SkipOverridden: true))
            .Concat(overrideFiles.Select(f => (File: f, SkipOverridden: false)))
            .Concat(PassageFiles(Path.Combine(moduleDirectory, "layouts")).Select(f => (File: f, SkipOverridden: false)));

        foreach (var (file, skipOverridden) in files)
        {
            var relative = Path.GetRelativePath(moduleDirectory, file).Replace('\\', '/');
            foreach (var doc in LoadDocuments(file))
            {
                var id = PassageId(doc);
                if (skipOverridden && id is not null && overrideIds.Contains(id))
                {
                    continue;
                }

                yield return new ModuleSourceDocument(id is null ? relative : $"{relative} ({id})", doc);
            }
        }
    }

    public static IEnumerable<YamlNode> LoadDocuments(string file)
    {
        var stream = new YamlStream();
        using (var reader = new StringReader(File.ReadAllText(file)))
        {
            stream.Load(reader);
        }

        return stream.Documents.Select(d => d.RootNode);
    }

    // Top-level files only, the same set ModuleLoader reads.
    private static IEnumerable<string> PassageFiles(string? directory) =>
        directory is not null && Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.mws.yaml").Order(StringComparer.Ordinal)
            : [];

    private static string? PassageId(YamlNode root) =>
        root is YamlMappingNode map && map.TryGet("passage_id") is YamlScalarNode s ? s.Value : null;
}
