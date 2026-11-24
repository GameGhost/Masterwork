using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>
/// One voice track declared in a <c>voices:</c> block: one reader's recordings of a module's
/// narration in one voice language. Its identity is (<see cref="Language"/>, <see cref="Id"/>), and
/// its files live at <c>assets/audio/vo/&lt;language&gt;/&lt;id&gt;/&lt;slug&gt;.&lt;ext&gt;</c>.
/// </summary>
public sealed record VoiceTrack
{
    /// <summary>The voice language, e.g. <c>en-US</c>.</summary>
    public required string Language { get; init; }

    /// <summary>The track id, unique within its language for one module.</summary>
    public required string Id { get; init; }

    /// <summary>The track's display name by UI locale. Empty when the manifest gave none.</summary>
    public IReadOnlyDictionary<string, string> Names { get; init; } = new Dictionary<string, string>();

    /// <summary>The folder holding this track's files, without a trailing slash.</summary>
    public string AssetFolder => $"assets/audio/vo/{Language}/{Id}";

    /// <summary>
    /// The name to show for <paramref name="uiLocale"/>: that locale's name, else the name in the
    /// track's own language, else the first name given, else <see cref="Id"/>.
    /// </summary>
    public string DisplayName(string? uiLocale)
    {
        if (uiLocale is not null && Names.TryGetValue(uiLocale, out var name))
        {
            return name;
        }

        if (Names.TryGetValue(Language, out var own))
        {
            return own;
        }

        return Names.Count > 0 ? Names.Values.First() : Id;
    }
}

/// <summary>Reads the <c>voices:</c> block shared by module and voice pack manifests.</summary>
internal static class VoicesBlock
{
    // A plain-scalar name is stored under this key; DisplayName falls through to it as "the first name".
    private const string UnlocalizedKey = "";

    /// <summary>Parses <c>voices:</c> from <paramref name="root"/>, keeping manifest order. Empty when absent.</summary>
    public static IReadOnlyList<VoiceTrack> Parse(YamlMappingNode root, YamlParseContext ctx)
    {
        if (root.GetMapping("voices", ctx) is not { } voicesMap)
        {
            return [];
        }

        var tracks = new List<VoiceTrack>();
        foreach (var (languageNode, tracksNode) in voicesMap.Children)
        {
            if (languageNode is not YamlScalarNode { Value: { Length: > 0 } language })
            {
                ctx.Warn("wrong_field_type", "voices: each key must be a language code; skipping an entry");
                continue;
            }

            if (tracksNode is not YamlSequenceNode trackList)
            {
                ctx.Warn("wrong_field_type", $"voices.{language}: expected a list of tracks but found a {YamlNodeExtensions.DescribeKind(tracksNode)}; skipping it");
                continue;
            }

            foreach (var child in trackList.Children)
            {
                if (child is not YamlMappingNode trackMap)
                {
                    ctx.Warn("wrong_field_type", $"voices.{language} entry: expected a mapping but found a {YamlNodeExtensions.DescribeKind(child)}; skipping it");
                    continue;
                }

                var id = trackMap.GetString("id", ctx);
                if (string.IsNullOrEmpty(id))
                {
                    ctx.Warn("missing_field", $"voices.{language} entry has no 'id'; skipping it");
                    continue;
                }

                trackMap.WarnUnmatchedFields(ctx, $"voices.{language} entry", "id", "name");
                if (tracks.Any(t => t.Language == language && t.Id == id))
                {
                    ctx.Warn("duplicate_voice_track", $"voices.{language} declares track '{id}' more than once; keeping the first");
                    continue;
                }

                tracks.Add(new VoiceTrack { Language = language, Id = id, Names = ReadNames(trackMap.TryGet("name"), language, ctx) });
            }
        }

        return tracks;
    }

    private static Dictionary<string, string> ReadNames(YamlNode? node, string language, YamlParseContext ctx)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        switch (node)
        {
            case null:
                break;
            case YamlScalarNode { Value: { } plain }:
                names[UnlocalizedKey] = plain;
                break;
            case YamlSequenceNode list:
                foreach (var entry in list.Children.OfType<YamlMappingNode>())
                {
                    foreach (var (k, v) in entry.Children)
                    {
                        if (k is YamlScalarNode { Value: { } locale } && v is YamlScalarNode { Value: { } text })
                        {
                            names[locale] = text;
                        }
                    }
                }

                break;
            default:
                ctx.Warn("wrong_field_type", $"voices.{language} entry 'name': expected a text value or a localized list; ignoring it");
                break;
        }

        return names;
    }
}
