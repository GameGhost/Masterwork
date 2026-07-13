using System.Text.Json;
using System.Text.RegularExpressions;

namespace Masterwork.Extractor;

// Converts TextMesh Pro <sprite="AtlasName" index=N> tags to asset_ref URIs.
// Loads the sprite-to-name mapping from ItemObtain JSON files.
public class SpriteMapper
{
    // Maps spriteName → icon:// URI (e.g. "01-bottle" → "icon://bottle")
    private readonly Dictionary<string, string> _spriteToUri = new(StringComparer.OrdinalIgnoreCase);

    // Regex for <sprite="AtlasName" index=N> and <sprite=AtlasName>
    private static readonly Regex SpriteTag = new(
        @"<sprite\s*=\s*""?([^""\s>]+)""?\s*(?:index\s*=\s*\d+)?\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // HTML-like layout/formatting tags. Captures the tag name so ConvertOrStripTag can decide
    // whether it maps to MWS markdown (b/i) or has no MWS equivalent and is dropped (everything else).
    private static readonly Regex HtmlTag = new(
        @"</?(line-height|align|size|color|b|i)[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // <b>/</b> -> ** and <i>/</i> -> _ (MWS markdown-style bold/italic — matches whatever Cradle's
    // own styleScope("bold"/"italic") idiom produces elsewhere); every other tag
    // (line-height/align/size/color) has no MWS equivalent and is dropped. Applies uniformly to
    // both the opening and closing tag, which is exactly what a symmetric markdown delimiter needs.
    private static string ConvertOrStripTag(Match m) => m.Groups[1].Value.ToLowerInvariant() switch
    {
        "b" => "**",
        "i" => "_",
        _ => "",
    };

    public static SpriteMapper Empty() => new();

    public static SpriteMapper FromJsonFile(string jsonPath)
    {
        var mapper = new SpriteMapper();
        try
        {
            var json = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(json);

            // Handle both array root and { "TheCostOfDisease": [...] } wrapper
            var root = doc.RootElement;
            JsonElement itemArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                itemArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // Use the first array-valued property
                itemArray = default;
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        itemArray = prop.Value;
                        break;
                    }
                }
                if (itemArray.ValueKind != JsonValueKind.Array)
                {
                    Console.Error.WriteLine($"Warning: no array found in sprite map JSON '{jsonPath}'");
                    return mapper;
                }
            }
            else
            {
                return mapper;
            }

            foreach (var item in itemArray.EnumerateArray())
            {
                // spriteName maps the atlas item name (e.g. "01-bottle")
                // identifier maps the logical name (e.g. "Bottle")
                var spriteName = item.TryGetProperty("spriteName", out var sn) ? sn.GetString() : null;
                var identifier = item.TryGetProperty("identifier", out var id) ? id.GetString() : null;
                var englishName = item.TryGetProperty("EnglishName", out var en) ? en.GetString() : null;
                var displayName = englishName ?? identifier ?? spriteName;

                if (!string.IsNullOrEmpty(spriteName) && !string.IsNullOrEmpty(displayName))
                {
                    mapper._spriteToUri[spriteName] = $"icon://{SlugifyName(displayName)}";
                }

                if (!string.IsNullOrEmpty(identifier))
                {
                    mapper._spriteToUri[identifier] = $"icon://{SlugifyName(displayName ?? identifier)}";
                }
            }
            Console.WriteLine($"Sprite map loaded: {mapper._spriteToUri.Count} entries from '{Path.GetFileName(jsonPath)}'");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: could not load sprite map from '{jsonPath}': {ex.Message}");
        }
        return mapper;
    }

    // Resolves a <sprite="AtlasName"> reference to an icon:// URI: direct sprite-map lookup first,
    // then — only if that atlas name ends in "Token" — a retry against the name with that suffix
    // stripped (e.g. "StorybookToken" isn't itself a sprite-map entry, but it's the same game piece
    // as "Storybook", which is; some call sites in the source reference it with the suffix, some
    // without). Falls back to a raw slug of the original atlas name when neither matches. Narrowly
    // scoped to an exact "Token" suffix specifically so it doesn't touch names that merely contain
    // "Token" without being an alias of a shorter known identifier (e.g. "S1_HeartToken" has no
    // "S1_Heart" sprite-map entry to fall back to, so it's unaffected and keeps its own slug).
    private string ResolveSpriteUri(string atlasName)
    {
        if (_spriteToUri.TryGetValue(atlasName, out var mapped))
        {
            return mapped;
        }

        if (atlasName.EndsWith("Token", StringComparison.OrdinalIgnoreCase))
        {
            var withoutSuffix = atlasName[..^"Token".Length];
            if (_spriteToUri.TryGetValue(withoutSuffix, out var mappedWithoutSuffix))
            {
                return mappedWithoutSuffix;
            }
        }

        return $"icon://{SlugifyName(atlasName)}";
    }

    // Returns null if text has no special tags (most text nodes).
    // Returns the parsed runs if there are sprite refs or style markup.
    public List<(string? Text, string? AssetRef)>? TryParseRichText(string raw)
    {
        if (!SpriteTag.IsMatch(raw) && !HtmlTag.IsMatch(raw))
        {
            return null;
        }

        var runs = new List<(string? Text, string? AssetRef)>();
        int pos = 0;
        foreach (Match m in SpriteTag.Matches(raw))
        {
            if (m.Index > pos)
            {
                var textSegment = HtmlTag.Replace(raw[pos..m.Index], ConvertOrStripTag).Trim();
                if (!string.IsNullOrEmpty(textSegment))
                {
                    runs.Add((textSegment, null));
                }
            }
            // Atlas name from <sprite="AtlasName" index=N> — try lookup, fall back to slug
            var atlasName = m.Groups[1].Value;
            runs.Add((null, ResolveSpriteUri(atlasName)));
            pos = m.Index + m.Length;
        }
        if (pos < raw.Length)
        {
            var remaining = HtmlTag.Replace(raw[pos..], ConvertOrStripTag).Trim();
            if (!string.IsNullOrEmpty(remaining))
            {
                runs.Add((remaining, null));
            }
        }
        return runs;
    }

    // Converts HTML-like layout/formatting tags to their MWS markdown equivalent where one exists
    // (b/i), strips the rest (spaces preserved — caller uses IsNullOrWhiteSpace).
    public string StripLayoutTags(string raw) => HtmlTag.Replace(raw, ConvertOrStripTag);

    // Returns true when raw is entirely an HTML-like layout tag with no other content,
    // and is not a sprite tag. Used to detect standalone per-call tokens like <align="center">.
    public bool IsStandaloneLayoutTag(string raw) =>
        HtmlTag.IsMatch(raw) &&
        string.IsNullOrWhiteSpace(HtmlTag.Replace(raw, "")) &&
        !SpriteTag.IsMatch(raw);

    private static string SlugifyName(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
}
