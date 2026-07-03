using System;
using System.Collections.Generic;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

// Small helper surface over YamlDotNet's representation model (YamlMappingNode / YamlSequenceNode /
// YamlScalarNode). The MWS v0.3 schema is small and fully known, so passages are parsed by hand
// against this low-level API rather than fighting a generic object-graph deserializer over a
// polymorphic (type:-dispatched) node list.
internal static class YamlNodeExtensions
{
    public static YamlNode? TryGet(this YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    public static string? GetString(this YamlMappingNode map, string key) =>
        map.TryGet(key) is YamlScalarNode s ? s.Value : null;

    public static string GetRequiredString(this YamlMappingNode map, string key) =>
        map.GetString(key) ?? throw new FormatException($"Missing required field '{key}'");

    public static bool GetBool(this YamlMappingNode map, string key, bool defaultValue = false)
    {
        var s = map.GetString(key);
        return s is null ? defaultValue : bool.Parse(s);
    }

    public static IReadOnlyList<string> GetStringList(this YamlMappingNode map, string key)
    {
        if (map.TryGet(key) is not YamlSequenceNode seq) return [];
        return seq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").ToList();
    }

    public static YamlMappingNode? GetMapping(this YamlMappingNode map, string key) =>
        map.TryGet(key) as YamlMappingNode;

    public static YamlSequenceNode? GetSequence(this YamlMappingNode map, string key) =>
        map.TryGet(key) as YamlSequenceNode;

    // Converts a scalar YAML node to its natural CLR type: bool, long, or string.
    // Used for switch-case `match:` values, which may be an int, string, or pattern string.
    public static object ToNaturalValue(this YamlNode node)
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
            return seq.Children.Select(ToNaturalValue).ToList();
        throw new FormatException($"Unsupported match value node type: {node.GetType().Name}");
    }
}
