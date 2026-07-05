using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

// Parses _variables.yaml into a flat name -> VarDef map. Standard variables (nameA-E, townname,
// players, playerCount, currentPassage) carry no type/default in the extracted file; the engine
// applies a fixed typing rule here: `players` is int, every other standard variable is string.
public static class VariableManifest
{
    public static IReadOnlyDictionary<string, VarDef> Parse(string yamlText, ModuleWarnings? warnings = null)
    {
        var ctx = new YamlParseContext(warnings, "_variables.yaml");

        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        var result = new Dictionary<string, VarDef>(StringComparer.Ordinal);

        // TODO: Determine what defines these, do the need to be in _variables.yaml?
        // Note: It looks like an early version of the original modules provided the
        //   module and player initialization. At some point it seem that was moved
        //   to the App itself, and theses variables became "standard" pre-initialized
        //   variables available to the modules.
        foreach (var name in root.GetStringList("standard_variables", ctx))
        {
            var isPlayers = name == "players";
            result[name] = new VarDef
            {
                Name = name,
                VarType = isPlayers ? "int" : "string",
                Default = isPlayers ? 0L : "",
                IsStandard = true,
            };
        }

        if (root.GetMapping("variables", ctx) is { } varsMap)
        {
            foreach (var (keyNode, valueNode) in varsMap.Children)
            {
                var name = keyNode is YamlScalarNode { Value: { } n } ? n : "";
                if (valueNode is not YamlMappingNode def)
                {
                    ctx.Warn("wrong_field_type", $"variable '{name}': expected a mapping but found a {YamlNodeExtensions.DescribeKind(valueNode)}; skipping it");
                    continue;
                }
                var varType = def.GetRequiredString("type", ctx);
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

        return result;
    }

    private static object? ParseDefaultValue(YamlNode node, string varType)
    {
        if (varType == "array")
        {
            return node is YamlSequenceNode seq
                ? seq.Children.OfType<YamlScalarNode>().Select(s => (object)(s.Value ?? "")).ToList()
                : [];
        }
        if (node is YamlScalarNode s)
        {
            return varType == "int"
                ? (long.TryParse(s.Value, out var l) ? l : 0L)
                : s.Value ?? "";
        }
        return null;
    }
}
