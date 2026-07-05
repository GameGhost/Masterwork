using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Parses a single <c>.mws.yaml</c> passage file (MWS v0.3) into an <see cref="MwsPassageDoc"/>
/// tree. Dispatches on each node's <c>type:</c> field against the low-level YAML representation
/// model — see <see cref="YamlNodeExtensions"/> for why this bypasses YamlDotNet's generic
/// object-graph deserializer.
/// </summary>
public static class PassageYamlParser
{
    private const string ExpectedFormat = "mws/0.3";

    /// <summary>
    /// Parses a passage document from raw YAML text.
    /// </summary>
    /// <param name="yamlText">The full contents of a <c>.mws.yaml</c> file.</param>
    /// <param name="warnings">
    /// Collector for non-fatal issues (unmatched fields, wrong-shaped fields, unknown node types,
    /// stale format versions). Pass <see langword="null"/> to discard warnings.
    /// </param>
    /// <exception cref="MwsParseException">
    /// The document is empty, or a required field is missing/malformed with no safe fallback.
    /// </exception>
    public static MwsPassageDoc ParsePassage(string yamlText, ModuleWarnings? warnings = null)
    {
        var ctx = new YamlParseContext(warnings);

        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        if (stream.Documents.Count == 0)
        {
            throw new MwsParseException("Empty YAML document");
        }

        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        // Grab passage_id up front (best-effort) purely so subsequent warnings/errors can be
        // attributed to it; GetRequiredString below still performs the real presence check.
        ctx.Source = root.GetString("passage_id", ctx) is { Length: > 0 } id ? id : "(unknown passage)";

        var format = root.GetRequiredString("format", ctx);
        if (format != ExpectedFormat)
        {
            ctx.Warn("unexpected_format_version", $"passage declares format '{format}', expected '{ExpectedFormat}' — may be stale output from an older extractor/hand-authored file");
        }

        Location? location = null;
        var locMap = root.GetMapping("location", ctx);
        if (locMap is not null)
        {
            location = new Location { Name = locMap.GetString("name", ctx), Icon = locMap.GetString("icon", ctx) };
            locMap.WarnUnmatchedFields(ctx, "passage location header", "name", "icon");
        }

        var passage = new MwsPassageDoc
        {
            PassageId = root.GetRequiredString("passage_id", ctx),
            Title = root.GetString("title", ctx),
            Layout = root.GetRequiredString("layout", ctx),
            Tags = root.GetStringList("tags", ctx),
            Debug = root.GetBool("debug", ctx),
            Location = location,
            CheckProgress = root.GetString("check_progress", ctx),
            Nodes = BuildNodeList(root.TryGet("nodes"), ctx, "passage nodes"),
        };

        root.WarnUnmatchedFields(ctx, "passage header",
            "format", "passage_id", "title", "tags", "layout", "debug", "location", "check_progress", "nodes");

        return passage;
    }

    // Yields only the mapping children of `seq`, warning about (and skipping) anything else —
    // e.g. a node list that accidentally contains a bare string or nested list.
    private static IEnumerable<YamlMappingNode> AsNodeMappings(YamlSequenceNode seq, YamlParseContext ctx, string label)
    {
        foreach (var child in seq.Children)
        {
            if (child is YamlMappingNode m)
            {
                yield return m;
            }
            else
            {
                ctx.Warn("wrong_field_type", $"{label}: expected a node mapping but found a {YamlNodeExtensions.DescribeKind(child)}; skipping it");
            }
        }
    }

    internal static List<Node> BuildNodeList(YamlNode? node, YamlParseContext ctx, string label = "node list")
    {
        if (node is null)
        {
            return [];
        }

        if (node is not YamlSequenceNode seq)
        {
            ctx.Warn("wrong_field_type", $"{label}: expected a list but found a {YamlNodeExtensions.DescribeKind(node)}; ignoring it");
            return [];
        }
        return [.. AsNodeMappings(seq, ctx, label).Select(m => BuildNode(m, ctx))];
    }

    internal static Node BuildNode(YamlMappingNode map, YamlParseContext ctx)
    {
        var type = map.GetRequiredString("type", ctx);
        switch (type)
        {
            case "text":
            {
                var result = new TextNode
                {
                    Value = map.GetRequiredString("value", ctx),
                    Align = ParseAlignment(map, ctx, "align"),
                    Lets = map.GetStringList("lets", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'text' node", "type", "value", "align", "lets");
                return result;
            }
            case "image":
            {
                var result = new ImageNode
                {
                    Asset = map.GetRequiredString("asset", ctx),
                    Size = map.GetString("size", ctx),
                    Align = ParseAlignment(map, ctx, "align"),
                };
                map.WarnUnmatchedFields(ctx, "'image' node", "type", "asset", "size", "align");
                return result;
            }
            case "break":
                map.WarnUnmatchedFields(ctx, "'break' node", "type");
                return new BreakNode();
            case "paragraph_break":
                map.WarnUnmatchedFields(ctx, "'paragraph_break' node", "type");
                return new ParagraphBreakNode();
            case "section":
            {
                var result = new SectionNode
                {
                    Title = map.GetString("title", ctx),
                    Style = map.GetString("style", ctx),
                    Collapsed = map.GetBool("collapsed", ctx),
                    Content = BuildNodeList(map.TryGet("content"), ctx, "'section' node content"),
                };
                map.WarnUnmatchedFields(ctx, "'section' node", "type", "title", "style", "collapsed", "content");
                return result;
            }
            case "let":
            {
                var result = new LetNode { Var = map.GetRequiredString("var", ctx), Expr = map.GetRequiredString("expr", ctx) };
                map.WarnUnmatchedFields(ctx, "'let' node", "type", "var", "expr");
                return result;
            }
            case "assign":
            {
                var result = new AssignNode { Var = map.GetRequiredString("var", ctx), Expr = map.GetRequiredString("expr", ctx) };
                map.WarnUnmatchedFields(ctx, "'assign' node", "type", "var", "expr");
                return result;
            }
            case "navigation":
            {
                var result = new NavigationNode
                {
                    Label = map.GetRequiredString("label", ctx),
                    Style = map.GetString("style", ctx),
                    Target = map.GetRequiredString("target", ctx),
                    StateAffecting = map.GetBool("state_affecting", ctx, defaultValue: true),
                    TimelineLabel = map.GetString("timeline_label", ctx),
                    OnClick = BuildNodeList(map.TryGet("onclick"), ctx, "'navigation' node onclick"),
                };
                map.WarnUnmatchedFields(ctx, "'navigation' node",
                    "type", "label", "style", "target", "state_affecting", "timeline_label", "onclick");
                return result;
            }
            case "popup":
            {
                var result = new PopupNode
                {
                    Label = map.GetString("label", ctx),
                    Style = map.GetString("style", ctx),
                    Layout = map.GetString("layout", ctx),
                    Content = BuildNodeList(map.TryGet("content"), ctx, "'popup' node content"),
                    OnClose = map.GetString("onclose", ctx),
                    Button = map.GetString("button", ctx),
                    StateAffecting = map.GetBool("state_affecting", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'popup' node",
                    "type", "label", "style", "layout", "content", "onclose", "button", "state_affecting");
                return result;
            }
            case "input":
            {
                var result = new InputNode
                {
                    Label = map.GetRequiredString("label", ctx),
                    Style = map.GetString("style", ctx),
                    Text = map.GetRequiredString("text", ctx),
                    InputType = ParseInputValueType(map, ctx, "input"),
                    Var = map.GetRequiredString("var", ctx),
                    OnSubmit = map.GetRequiredString("onsubmit", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'input' node", "type", "label", "style", "text", "input", "var", "onsubmit");
                return result;
            }
            case "prompt":
            {
                var result = new PromptNode
                {
                    Text = map.GetRequiredString("text", ctx),
                    InputType = ParseInputValueType(map, ctx, "input"),
                    Var = map.GetRequiredString("var", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'prompt' node", "type", "text", "input", "var");
                return result;
            }
            case "goto":
            {
                var result = new GotoNode { Target = map.GetRequiredString("target", ctx) };
                map.WarnUnmatchedFields(ctx, "'goto' node", "type", "target");
                return result;
            }
            case "include_passage":
            {
                var result = new IncludePassageNode { Target = map.GetRequiredString("target", ctx) };
                map.WarnUnmatchedFields(ctx, "'include_passage' node", "type", "target");
                return result;
            }
            case "conditional":
                return BuildConditional(map, ctx);
            case "switch":
                return BuildSwitch(map, ctx);
            case "foreach":
            {
                var result = new ForEachNode
                {
                    Var = map.GetRequiredString("var", ctx),
                    In = map.GetRequiredString("in", ctx),
                    Do = BuildNodeList(map.TryGet("do"), ctx, "'foreach' node do"),
                };
                map.WarnUnmatchedFields(ctx, "'foreach' node", "type", "var", "in", "do");
                return result;
            }
            case "checkpoint":
            {
                var result = new CheckpointNode
                {
                    Id = map.GetRequiredString("id", ctx),
                    Display = map.GetString("display", ctx),
                    Diagnostic = map.GetString("diagnostic", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'checkpoint' node", "type", "id", "display", "diagnostic");
                return result;
            }
            case "record":
            {
                var result = new RecordNode { Id = map.GetRequiredString("id", ctx) };
                map.WarnUnmatchedFields(ctx, "'record' node", "type", "id");
                return result;
            }
            default:
                ctx.Warn("unknown_node_type", $"unrecognized node type '{type}'");
                return new UnknownNode(type);
        }
    }

    private static ConditionalNode BuildConditional(YamlMappingNode map, YamlParseContext ctx)
    {
        // Flat form: if: + then: directly on the node.
        var flatIf = map.GetString("if", ctx);
        if (flatIf is not null)
        {
            var flatResult = new ConditionalNode
            {
                Conditions = [new ConditionalBranch { If = flatIf, Then = BuildNodeList(map.TryGet("then"), ctx, "'conditional' node then") }],
                Else = null,
            };
            map.WarnUnmatchedFields(ctx, "'conditional' node", "type", "if", "then");
            return flatResult;
        }

        // Multi-branch form: conditions: [{if, then}] + optional else:.
        var conditions = new List<ConditionalBranch>();
        if (map.GetSequence("conditions", ctx) is { } seq)
        {
            foreach (var branchNode in AsNodeMappings(seq, ctx, "'conditional' node conditions entry"))
            {
                conditions.Add(new ConditionalBranch
                {
                    If = branchNode.GetRequiredString("if", ctx),
                    Then = BuildNodeList(branchNode.TryGet("then"), ctx, "'conditional' node conditions[].then"),
                });
                branchNode.WarnUnmatchedFields(ctx, "'conditional' node conditions entry", "if", "then");
            }
        }
        var elseNode = map.TryGet("else");
        var result = new ConditionalNode
        {
            Conditions = conditions,
            Else = elseNode is null ? null : BuildNodeList(elseNode, ctx, "'conditional' node else"),
        };
        map.WarnUnmatchedFields(ctx, "'conditional' node", "type", "conditions", "else");
        return result;
    }

    private static SwitchNode BuildSwitch(YamlMappingNode map, YamlParseContext ctx)
    {
        var cases = new List<SwitchCase>();
        if (map.GetSequence("cases", ctx) is { } seq)
        {
            foreach (var caseNode in AsNodeMappings(seq, ctx, "'switch' node cases entry"))
            {
                var matchNode = caseNode.TryGet("match");
                if (matchNode is null)
                {
                    throw new MwsParseException($"{ctx.Source}: switch case missing 'match'");
                }

                cases.Add(new SwitchCase
                {
                    Match = matchNode.ToNaturalValue(ctx),
                    Nodes = BuildNodeList(caseNode.TryGet("nodes"), ctx, "'switch' node cases[].nodes"),
                });
                caseNode.WarnUnmatchedFields(ctx, "'switch' node cases entry", "match", "nodes");
            }
        }
        var defaultNode = map.TryGet("default");
        var result = new SwitchNode
        {
            On = map.GetRequiredString("on", ctx),
            Cases = cases,
            Default = defaultNode is null ? null : BuildNodeList(defaultNode, ctx, "'switch' node default"),
        };
        map.WarnUnmatchedFields(ctx, "'switch' node", "type", "on", "cases", "default");
        return result;
    }

    private static Alignment? ParseAlignment(YamlMappingNode map, YamlParseContext ctx, string key)
    {
        var raw = map.GetString(key, ctx);
        if (raw is null)
        {
            return null;
        }

        var value = raw switch
        {
            "left" => Alignment.Left,
            "center" => Alignment.Center,
            "right" => Alignment.Right,
            "justified" => Alignment.Justified,
            _ => (Alignment?)null,
        };
        if (value is null)
        {
            ctx.Warn("invalid_enum_value", $"field '{key}' has unrecognized alignment '{raw}'; falling back to default");
        }

        return value;
    }

    // input.input / prompt.input has no sensible fallback — an unrecognized value here would
    // silently pick a wrong input widget for the player, so this is a hard module load error.
    private static InputValueType ParseInputValueType(YamlMappingNode map, YamlParseContext ctx, string key)
    {
        var raw = map.GetRequiredString(key, ctx);
        return raw switch
        {
            "string" => InputValueType.String,
            "number" => InputValueType.Number,
            _ => throw new MwsParseException($"{ctx.Source}: field '{key}' has invalid input type '{raw}' (expected 'string' or 'number')"),
        };
    }
}
