using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>The module a voice pack voices (<c>for:</c>).</summary>
public sealed record VoicePackTarget
{
    /// <summary>The voiced module's <see cref="ModuleManifest.Id"/>.</summary>
    public required string ModuleId { get; init; }

    /// <summary>The oldest module version the pack works with.</summary>
    public required string MinVersion { get; init; }
}

/// <summary>
/// A voice pack's <c>manifest.yaml</c> (<c>type: 'voices'</c>): an optional package that adds voice
/// tracks to one module. The module never lists the pack; the pack names the module in
/// <see cref="For"/>. Its files use the module's own layout,
/// <c>assets/audio/vo/&lt;language&gt;/&lt;track&gt;/&lt;slug&gt;.&lt;ext&gt;</c>.
/// </summary>
public sealed record VoicePackManifest
{
    /// <summary>The pack's own id.</summary>
    public required string Id { get; init; }

    /// <summary>Display title, plain text.</summary>
    public required string Title { get; init; }

    /// <summary>Human-readable description, plain text.</summary>
    public string? Description { get; init; }

    /// <summary>The pack's own version.</summary>
    public required string Version { get; init; }

    /// <summary>The module this pack voices.</summary>
    public required VoicePackTarget For { get; init; }

    /// <summary>The tracks this pack adds, in manifest order.</summary>
    public IReadOnlyList<VoiceTrack> Voices { get; init; } = [];

    /// <summary>The declared MWS format version, if any.</summary>
    public string? Format { get; init; }
}

/// <summary>Parses a voice pack's <c>manifest.yaml</c> into a <see cref="VoicePackManifest"/>.</summary>
public sealed class VoicePackManifestParser
{
    /// <summary>The <c>type:</c> value that marks a voice pack.</summary>
    public const string PackageType = "voices";

    /// <summary>Parses <paramref name="yamlText"/>, recording non-fatal problems in <paramref name="warnings"/>.</summary>
    /// <exception cref="MwsParseException">A required field (<c>id</c>, <c>title</c>, <c>version</c>, <c>for.id</c>, <c>for.min_version</c>) is missing.</exception>
    public VoicePackManifest Parse(string yamlText, ModuleWarnings? warnings = null)
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

        var type = root.GetString("type", ctx);
        if (type is not null && type != PackageType)
        {
            ctx.Warn("invalid_enum_value", $"voice pack manifest declares type '{type}', expected '{PackageType}'");
        }

        if (root.TryGet("for") is not YamlMappingNode forMap)
        {
            throw new MwsParseException($"{ctx.Source}: missing required mapping 'for' (the module id and minimum version this pack voices)");
        }

        var target = new VoicePackTarget
        {
            ModuleId = forMap.GetRequiredString("id", ctx),
            MinVersion = forMap.GetRequiredString("min_version", ctx),
        };
        forMap.WarnUnmatchedFields(ctx, "for", "id", "min_version");

        var manifest = new VoicePackManifest
        {
            Id = root.GetRequiredString("id", ctx),
            Title = root.GetRequiredString("title", ctx),
            Description = root.GetString("description", ctx),
            Version = root.GetRequiredString("version", ctx),
            For = target,
            Voices = VoicesBlock.Parse(root, ctx),
            Format = format,
        };

        root.WarnUnmatchedFields(ctx, "manifest.yaml", "type", "format", "id", "title", "description", "version", "for", "voices");
        return manifest;
    }
}
