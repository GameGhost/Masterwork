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

        var format = root.GetString("format", ctx);
        if (format is not null && format != MwsFormatVersion.Current)
        {
            ctx.Warn("unexpected_format_version", $"manifest declares format '{format}', expected '{MwsFormatVersion.Current}' — may be stale output from an older extractor/hand-authored file");
        }

        var manifest = new AssetPackManifest
        {
            Id = root.GetRequiredString("id", ctx),
            Title = root.GetRequiredString("title", ctx),
            Description = root.GetString("description", ctx),
            Version = root.GetRequiredString("version", ctx),
            DefaultLocale = root.GetString("default_locale", ctx) ?? ModuleLocales.Default,
            Format = format,
        };

        // "type" isn't captured onto AssetPackManifest — an asset pack's type is always implicitly
        // "assets" by virtue of being parsed by this class at all — but it's tolerated here (not
        // flagged as unmatched) since callers that need to tell a module and an asset pack
        // manifest.yaml apart before knowing which parser to use (e.g. an upload flow accepting
        // either) read this same field via ManifestParser's ModuleType first.
        root.WarnUnmatchedFields(ctx, "manifest.yaml", "type", "format", "id", "title", "description", "version", "default_locale");

        return manifest;
    }
}
