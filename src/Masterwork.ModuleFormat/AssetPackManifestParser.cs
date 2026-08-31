using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <inheritdoc cref="IAssetPackManifestParser"/>
public sealed class AssetPackManifestParser : IAssetPackManifestParser
{
    /// <inheritdoc/>
    public AssetPackManifest Parse(string yamlText, ModuleWarnings? warnings = null)
    {
        var ctx = new YamlParseContext(warnings, "manifest.yaml");

        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        var manifest = new AssetPackManifest
        {
            Id = root.GetRequiredString("id", ctx),
            Title = root.GetRequiredString("title", ctx),
            Description = root.GetString("description", ctx),
            Version = root.GetRequiredString("version", ctx),
            DefaultLocale = root.GetString("default_locale", ctx) ?? ModuleLocales.Default,
        };

        root.WarnUnmatchedFields(ctx, "manifest.yaml", "id", "title", "description", "version", "default_locale");

        return manifest;
    }
}
