using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Small helper surface over YamlDotNet's representation model (<see cref="YamlMappingNode"/>,
/// <see cref="YamlSequenceNode"/>, <see cref="YamlScalarNode"/>). The MWS v0.3 schema is small and
/// fully known, so passages are parsed by hand against this low-level API rather than fighting a
/// generic object-graph deserializer over a polymorphic (<c>type:</c>-dispatched) node list.
/// </summary>
/// <remarks>
/// Error/warning policy:
/// <list type="bullet">
/// <item>
/// A required field that's absent, or present with the wrong shape, throws
/// <see cref="MwsParseException"/> — there's no sensible fallback value.
/// </item>
/// <item>
/// An optional field present with the wrong shape (e.g. a mapping where a plain string was
/// expected) logs a "wrong_field_type" warning to <see cref="ModuleWarnings"/> and falls back to
/// the same default as if the field were simply absent.
/// </item>
/// <item>
/// <c>debug</c>/<c>collapsed</c> etc. that hold non-boolean text also throw
/// <see cref="MwsParseException"/> — a silently-wrong default here would corrupt
/// navigation/timeline semantics, so this is treated as a hard error rather than a warning.
/// <c>link</c>/<c>popup</c>'s own <c>snapshot</c> field is the deliberate exception: any non-bool
/// text is accepted as a custom timeline label (see <see cref="GetBoolOrLabel"/>).
/// </item>
/// </list>
/// </remarks>
internal static class YamlNodeExtensions
{
    /// <summary>Looks up a key on a mapping node, or <see langword="null"/> if absent.</summary>
    public static YamlNode? TryGet(this YamlMappingNode map, string key) =>
        map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;

    /// <summary>
    /// Reads an optional string field. Logs a "wrong_field_type" warning and returns
    /// <see langword="null"/> if the field is present but not a scalar.
    /// </summary>
    public static string? GetString(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null)
        {
            return null;
        }

        if (node is YamlScalarNode s)
        {
            return s.Value;
        }

        ctx.Warn("wrong_field_type", $"field '{key}' expected a text value but found a {DescribeKind(node)}; ignoring it");
        return null;
    }

    /// <summary>
    /// Reads a required string field.
    /// </summary>
    /// <exception cref="MwsParseException">The field is missing, or present with the wrong shape.</exception>
    public static string GetRequiredString(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null)
        {
            throw new MwsParseException($"{ctx.Source}: missing required field '{key}'");
        }

        if (node is YamlScalarNode { Value: { } value })
        {
            return value;
        }

        throw new MwsParseException($"{ctx.Source}: field '{key}' must be a text value but found a {DescribeKind(node)}");
    }

    /// <summary>
    /// Reads a boolean field, returning <paramref name="defaultValue"/> if absent.
    /// </summary>
    /// <exception cref="MwsParseException">The field is present but not literally "true"/"false".</exception>
    public static bool GetBool(this YamlMappingNode map, string key, YamlParseContext ctx, bool defaultValue = false)
    {
        var raw = map.GetString(key, ctx);
        if (raw is null)
        {
            return defaultValue;
        }

        if (bool.TryParse(raw, out var value))
        {
            return value;
        }

        throw new MwsParseException($"{ctx.Source}: field '{key}' must be 'true' or 'false' but found '{raw}'");
    }

    /// <summary>
    /// Reads a field that's either a bare boolean or an arbitrary string, returning
    /// <paramref name="defaultValue"/> (with no label) if absent. A string value means
    /// <see langword="true"/> for behavior, with the string itself as the label — used by
    /// <c>link</c>/<c>popup</c>'s <c>snapshot</c> field to fold a custom timeline label into the
    /// same flag that decides whether a snapshot is created at all.
    /// </summary>
    public static (bool Value, string? Label) GetBoolOrLabel(this YamlMappingNode map, string key, YamlParseContext ctx, bool defaultValue = false)
    {
        var raw = map.GetString(key, ctx);
        if (raw is null)
        {
            return (defaultValue, null);
        }

        return bool.TryParse(raw, out var value) ? (value, null) : (true, raw);
    }

    /// <summary>
    /// Reads an optional integer field, returning <see langword="null"/> if absent.
    /// </summary>
    /// <exception cref="MwsParseException">The field is present but not a valid integer.</exception>
    public static int? GetInt(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var raw = map.GetString(key, ctx);
        if (raw is null)
        {
            return null;
        }

        if (int.TryParse(raw, out var value))
        {
            return value;
        }

        throw new MwsParseException($"{ctx.Source}: field '{key}' must be an integer but found '{raw}'");
    }

    /// <summary>
    /// Reads an optional 64-bit integer field, returning <see langword="null"/> if absent.
    /// </summary>
    /// <exception cref="MwsParseException">The field is present but not a valid integer.</exception>
    public static long? GetLong(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var raw = map.GetString(key, ctx);
        if (raw is null)
        {
            return null;
        }

        if (long.TryParse(raw, out var value))
        {
            return value;
        }

        throw new MwsParseException($"{ctx.Source}: field '{key}' must be an integer but found '{raw}'");
    }

    /// <summary>
    /// Reads a list-of-strings field. Logs "wrong_field_type" and returns an empty list if the
    /// field isn't a sequence; individual non-scalar elements are skipped with their own warning.
    /// </summary>
    public static IReadOnlyList<string> GetStringList(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null)
        {
            return [];
        }

        if (node is not YamlSequenceNode seq)
        {
            ctx.Warn("wrong_field_type", $"field '{key}' expected a list but found a {DescribeKind(node)}; ignoring it");
            return [];
        }
        var result = new List<string>(seq.Children.Count);
        foreach (var child in seq.Children)
        {
            if (child is YamlScalarNode s)
            {
                result.Add(s.Value ?? "");
            }
            else
            {
                ctx.Warn("wrong_field_type", $"field '{key}' contains a non-text element ({DescribeKind(child)}); skipping it");
            }
        }
        return result;
    }

    /// <summary>
    /// Reads an optional mapping field. Logs "wrong_field_type" and returns <see langword="null"/>
    /// if the field is present but not a mapping.
    /// </summary>
    public static YamlMappingNode? GetMapping(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null)
        {
            return null;
        }

        if (node is YamlMappingNode m)
        {
            return m;
        }

        ctx.Warn("wrong_field_type", $"field '{key}' expected a mapping but found a {DescribeKind(node)}; ignoring it");
        return null;
    }

    /// <summary>
    /// Reads an optional sequence field. Logs "wrong_field_type" and returns <see langword="null"/>
    /// if the field is present but not a sequence.
    /// </summary>
    public static YamlSequenceNode? GetSequence(this YamlMappingNode map, string key, YamlParseContext ctx)
    {
        var node = map.TryGet(key);
        if (node is null)
        {
            return null;
        }

        if (node is YamlSequenceNode seq)
        {
            return seq;
        }

        ctx.Warn("wrong_field_type", $"field '{key}' expected a list but found a {DescribeKind(node)}; ignoring it");
        return null;
    }

    /// <summary>
    /// Warns about any keys present on <paramref name="map"/> that weren't consumed while building
    /// whatever <paramref name="label"/> describes (e.g. "'text' node", "passage header") —
    /// catches typos and stale/unknown fields left over from hand-written YAML or an older
    /// extractor version.
    /// </summary>
    public static void WarnUnmatchedFields(this YamlMappingNode map, YamlParseContext ctx, string label, params string[] expectedKeys)
    {
        foreach (var keyNode in map.Children.Keys)
        {
            if (keyNode is YamlScalarNode { Value: { } key } && !expectedKeys.Contains(key))
            {
                ctx.Warn("unmatched_field", $"{label}: unrecognized field '{key}'");
            }
        }
    }

    /// <summary>
    /// Converts a scalar YAML node to its natural CLR type: <see cref="bool"/>, <see cref="long"/>,
    /// or <see cref="string"/> (or a <see cref="List{T}"/> of those for a sequence). Used for
    /// switch-case <c>match:</c> values, which may be an int, string, or pattern string.
    /// </summary>
    /// <exception cref="MwsParseException">The node is neither a scalar nor a sequence.</exception>
    public static object ToNaturalValue(this YamlNode node, YamlParseContext ctx)
    {
        if (node is YamlScalarNode s)
        {
            var v = s.Value ?? "";
            if (s.Style == YamlDotNet.Core.ScalarStyle.Plain)
            {
                if (long.TryParse(v, out var l))
                {
                    return l;
                }

                if (bool.TryParse(v, out var b))
                {
                    return b;
                }
            }
            return v;
        }
        if (node is YamlSequenceNode seq)
        {
            return seq.Children.Select(c => c.ToNaturalValue(ctx)).ToList();
        }

        throw new MwsParseException($"{ctx.Source}: switch case 'match' has an unsupported value shape ({DescribeKind(node)})");
    }

    /// <summary>Describes a YAML node's shape for use in warning/error messages.</summary>
    internal static string DescribeKind(YamlNode node) => node switch
    {
        YamlMappingNode => "mapping",
        YamlSequenceNode => "list",
        YamlScalarNode => "empty value",
        _ => node.GetType().Name,
    };
}
