using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>
/// <inheritdoc cref="IVariableManifest"/> Each entry under <c>variables:</c> is normally a bare
/// <c>name: type</c> line — the engine seeds it with its type's canonical zero value (empty
/// string, <c>0</c>, <c>false</c>, empty record, empty array) at session start. A variable only
/// gets the expanded <c>name: {type: ..., default: ...}</c> form when it needs a non-canonical
/// starting value, which the extractor only ever derives from an actual Cradle <c>VarDefs</c>
/// field initializer (never from a heuristic guess at what a variable's "real" starting value
/// might be — see <c>CradleExtractor</c>'s variable-discovery pass).
///
/// <c>standard_variables:</c> is a legacy-compat read path only — the extractor no longer
/// generates it (every discovered variable, including nameA-E, townname, players, etc., now gets
/// a real inferred type in <c>variables:</c> instead). It's still parsed here so
/// <c>_variables.yaml</c> files from modules not yet re-extracted since that change keep loading:
/// those bare names carry no type of their own, so the engine applies a fixed typing rule —
/// <c>players</c> is <c>int</c>, every other standard variable is <c>string</c> — which was always
/// a guess, not something derived from the source.
/// </summary>
public sealed class VariableManifest : IVariableManifest
{
    private readonly ILogger<VariableManifest> _logger;

    /// <summary>Creates a manifest parser that discards log output.</summary>
    public VariableManifest() : this(NullLogger<VariableManifest>.Instance)
    {
    }

    /// <summary>Creates a manifest parser that logs through <paramref name="logger"/>.</summary>
    public VariableManifest(ILogger<VariableManifest> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, VarDef> Parse(string yamlText, ModuleWarnings? warnings = null)
    {
        var ctx = new YamlParseContext(warnings, "_variables.yaml", _logger);

        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        var result = new Dictionary<string, VarDef>(StringComparer.Ordinal);

        // Legacy compat: modules extracted before the extractor stopped special-casing these
        // names may still have a populated standard_variables: list. Re-extracting a module
        // (or hand-writing a fresh _variables.yaml) makes this an empty list — see the class
        // doc comment above.
        foreach (var name in root.GetStringList("standard_variables", ctx))
        {
            result[name] = new VarDef
            {
                Name = name,
                VarType = name == "players" ? VarKind.Integer : VarKind.String,
                IsStandard = true,
            };
        }

        if (root.GetMapping("variables", ctx) is { } varsMap)
        {
            foreach (var (keyNode, valueNode) in varsMap.Children)
            {
                var name = keyNode is YamlScalarNode { Value: { } n } ? n : "";

                if (valueNode is YamlScalarNode { Value: { } typeText })
                {
                    // Common case: `name: type` — no explicit default, canonical zero applies.
                    if (!TryParseVarKind(typeText, name, ctx, out var kind))
                    {
                        continue;
                    }

                    result[name] = new VarDef { Name = name, VarType = kind, IsStandard = false };
                    continue;
                }

                if (valueNode is not YamlMappingNode def)
                {
                    ctx.Warn("wrong_field_type", $"variable '{name}': expected a type name or a mapping but found a {YamlNodeExtensions.DescribeKind(valueNode)}; skipping it");
                    continue;
                }

                var typeName = def.GetRequiredString("type", ctx);
                if (!TryParseVarKind(typeName, name, ctx, out var varType))
                {
                    continue;
                }

                var defaultNode = def.TryGet("default");
                result[name] = new VarDef
                {
                    Name = name,
                    VarType = varType,
                    Default = defaultNode is null ? null : ParseDefaultValue(defaultNode, varType),
                    IsStandard = false,
                };
                def.WarnUnmatchedFields(ctx, $"variable '{name}'", "type", "default");
            }
        }

        root.WarnUnmatchedFields(ctx, "_variables.yaml", "standard_variables", "variables");

        _logger.LogDebug("Parsed variable manifest: {Count} variables", result.Count);
        return result;
    }

    // A malformed type name means the variable would otherwise silently fall back to a wrong
    // declared type (and thus a wrong seeded default) — treated as a warning + skip, not a hard
    // parse failure, so one bad entry doesn't take down the whole module's variable manifest.
    private static bool TryParseVarKind(string typeText, string name, YamlParseContext ctx, out VarKind kind)
    {
        try
        {
            kind = VarKindText.Parse(typeText);
            return true;
        }
        catch (FormatException)
        {
            ctx.Warn("wrong_field_type", $"variable '{name}': unrecognized type '{typeText}'; skipping it");
            kind = default;
            return false;
        }
    }

    private static object? ParseDefaultValue(YamlNode node, VarKind varType)
    {
        if (varType is VarKind.StringArray or VarKind.IntArray or VarKind.BooleanArray or VarKind.RecordArray)
        {
            return node is YamlSequenceNode seq
                ? seq.Children.OfType<YamlScalarNode>().Select(s => (object)(s.Value ?? "")).ToList()
                : [];
        }

        if (node is not YamlScalarNode s)
        {
            return null;
        }

        return varType switch
        {
            VarKind.Integer => long.TryParse(s.Value, out var l) ? l : 0L,
            VarKind.Boolean => bool.TryParse(s.Value, out var b) && b,
            _ => s.Value ?? "",
        };
    }
}
