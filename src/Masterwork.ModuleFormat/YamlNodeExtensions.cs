using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

// Small helper surface over YamlDotNet's representation model (YamlMappingNode / YamlSequenceNode /
// YamlScalarNode). The MWS v0.3 schema is small and fully known, so passages are parsed by hand
// against this low-level API rather than fighting a generic object-graph deserializer over a
// polymorphic (type:-dispatched) node list.
//
// Error/warning policy:
//   - A required field that's absent, or present with the wrong shape, throws MwsParseException —
//     there's no sensible fallback value.
//   - An optional field present with the wrong shape (e.g. a mapping where a plain string was
//     expected) logs a "wrong_field_type" warning to ModuleWarnings and falls back to the same
//     default as if the field were simply absent.
//   - `state_affecting`/`debug`/`collapsed` etc. that hold non-boolean text also throw
//     MwsParseException — a silently-wrong default here would corrupt navigation/timeline
//     semantics, so this is treated as a hard error rather than a warning.
internal static class YamlNodeExtensions
{
    public static YamlNode? TryGet(this YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    public static string? GetString(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null) return null;
        if (node is YamlScalarNode s) return s.Value;
        ctx.Warn("wrong_field_type", $"field '{key}' expected a text value but found a {DescribeKind(node)}; ignoring it");
        return null;
    }

    public static string GetRequiredString(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null)
            throw new MwsParseException($"{ctx.Source}: missing required field '{key}'");
        if (node is YamlScalarNode { Value: { } value })
            return value;
        throw new MwsParseException($"{ctx.Source}: field '{key}' must be a text value but found a {DescribeKind(node)}");
    }

    public static bool GetBool(this YamlMappingNode map, string key, YamlParseContext ctx, bool defaultValue = false)
    {
        var raw = map.GetString(key, ctx);
        if (raw is null) return defaultValue;
        if (bool.TryParse(raw, out var value)) return value;
        throw new MwsParseException($"{ctx.Source}: field '{key}' must be 'true' or 'false' but found '{raw}'");
    }

    public static IReadOnlyList<string> GetStringList(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null) return [];
        if (node is not YamlSequenceNode seq)
        {
            ctx.Warn("wrong_field_type", $"field '{key}' expected a list but found a {DescribeKind(node)}; ignoring it");
            return [];
        }
        var result = new List<string>(seq.Children.Count);
        foreach (var child in seq.Children)
        {
            if (child is YamlScalarNode s) result.Add(s.Value ?? "");
            else ctx.Warn("wrong_field_type", $"field '{key}' contains a non-text element ({DescribeKind(child)}); skipping it");
        }
        return result;
    }

    public static YamlMappingNode? GetMapping(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null) return null;
        if (node is YamlMappingNode m) return m;
        ctx.Warn("wrong_field_type", $"field '{key}' expected a mapping but found a {DescribeKind(node)}; ignoring it");
        return null;
    }

    public static YamlSequenceNode? GetSequence(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null) return null;
        if (node is YamlSequenceNode seq) return seq;
        ctx.Warn("wrong_field_type", $"field '{key}' expected a list but found a {DescribeKind(node)}; ignoring it");
        return null;
    }

    // Warns about any keys present on `map` that weren't consumed while building whatever `label`
    // describes (e.g. "'text' node", "passage header") — catches typos and stale/unknown fields
    // left over from hand-written YAML or an older extractor version.
    public static void WarnUnmatchedFields(this YamlMappingNode map, YamlParseContext ctx, string label, params string[] expectedKeys)
    {
        foreach (var keyNode in map.Children.Keys)
        {
            if (keyNode is YamlScalarNode { Value: { } key } && !expectedKeys.Contains(key))
                ctx.Warn("unmatched_field", $"{label}: unrecognized field '{key}'");
        }
    }

    // Converts a scalar YAML node to its natural CLR type: bool, long, or string.
    // Used for switch-case `match:` values, which may be an int, string, or pattern string.
    public static object ToNaturalValue(this YamlNode node, YamlParseContext ctx)
    {
        if (node is YamlScalarNode s)
        {
            var v = s.Value ?? "";
            if (s.Style == YamlDotNet.Core.ScalarStyle.Plain)
            {
                if (long.TryParse(v, out var l)) return l;
                if (bool.TryParse(v, out var b)) return b;
            }
            return v;
        }
        if (node is YamlSequenceNode seq)
            return seq.Children.Select(c => c.ToNaturalValue(ctx)).ToList();
        throw new MwsParseException($"{ctx.Source}: switch case 'match' has an unsupported value shape ({DescribeKind(node)})");
    }

    internal static string DescribeKind(YamlNode node) => node switch
    {
        YamlMappingNode => "mapping",
        YamlSequenceNode => "list",
        YamlScalarNode => "empty value",
        _ => node.GetType().Name,
    };
}
