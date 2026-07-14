using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>
/// <inheritdoc cref="IPassageYamlParser"/> Dispatches on each node's <c>type:</c> field against
/// the low-level YAML representation model — see <see cref="YamlNodeExtensions"/> for why this
/// bypasses YamlDotNet's generic object-graph deserializer.
/// </summary>
public sealed class PassageYamlParser : IPassageYamlParser
{
    private const string ExpectedFormat = "mws/0.4";

    private readonly ILogger<PassageYamlParser> _logger;

    /// <summary>Creates a parser that discards log output.</summary>
    public PassageYamlParser() : this(NullLogger<PassageYamlParser>.Instance)
    {
    }

    /// <summary>Creates a parser that logs through <paramref name="logger"/>.</summary>
    public PassageYamlParser(ILogger<PassageYamlParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public MwsPassageDoc ParsePassage(string yamlText, ModuleWarnings? warnings = null)
    {
        var ctx = new YamlParseContext(warnings, logger: _logger);

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
        _logger.LogDebug("Parsing passage '{PassageId}'", ctx.Source);

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
            Subtitle = root.GetString("subtitle", ctx),
            Layout = root.GetRequiredString("layout", ctx),
            Tags = root.GetStringList("tags", ctx),
            Debug = root.GetBool("debug", ctx),
            Location = location,
            CheckProgress = root.GetString("check_progress", ctx),
            Ending = root.GetBool("ending", ctx),
            Nodes = BuildNodeList(root.TryGet("nodes"), ctx, "passage nodes"),
        };

        root.WarnUnmatchedFields(ctx, "passage header",
            "format", "passage_id", "title", "subtitle", "tags", "layout", "debug", "location", "check_progress", "ending", "nodes");

        _logger.LogDebug("Parsed passage '{PassageId}' with {NodeCount} top-level nodes", passage.PassageId, passage.Nodes.Count);
        return passage;
    }

    /// <inheritdoc/>
    public LayoutChromeDoc ParseLayoutChrome(string yamlText, ModuleWarnings? warnings = null)
    {
        var ctx = new YamlParseContext(warnings, logger: _logger);

        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        if (stream.Documents.Count == 0)
        {
            throw new MwsParseException("Empty YAML document");
        }

        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        ctx.Source = root.GetString("layout_id", ctx) is { Length: > 0 } id ? id : "(unknown layout)";
        _logger.LogDebug("Parsing layout chrome '{LayoutId}'", ctx.Source);

        var format = root.GetRequiredString("format", ctx);
        if (format != ExpectedFormat)
        {
            ctx.Warn("unexpected_format_version", $"layout chrome declares format '{format}', expected '{ExpectedFormat}' — may be stale output from an older extractor/hand-authored file");
        }

        var chrome = new LayoutChromeDoc
        {
            LayoutId = root.GetRequiredString("layout_id", ctx),
            Header = BuildNodeList(root.TryGet("header"), ctx, "layout chrome header"),
            Footer = BuildNodeList(root.TryGet("footer"), ctx, "layout chrome footer"),
            BeforeContent = BuildNodeList(root.TryGet("before_content"), ctx, "layout chrome before_content"),
            AfterContent = BuildNodeList(root.TryGet("after_content"), ctx, "layout chrome after_content"),
        };

        root.WarnUnmatchedFields(ctx, "layout chrome",
            "format", "layout_id", "header", "footer", "before_content", "after_content");

        _logger.LogDebug("Parsed layout chrome '{LayoutId}'", chrome.LayoutId);
        return chrome;
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

    private static List<Node> BuildNodeList(YamlNode? node, YamlParseContext ctx, string label = "node list")
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

    private static Node BuildNode(YamlMappingNode map, YamlParseContext ctx)
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
                    Style = map.GetString("style", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'text' node", "type", "value", "align", "lets", "style");
                return result;
            }
            case "image":
            {
                var result = new ImageNode
                {
                    Asset = map.GetRequiredString("asset", ctx),
                    Size = map.GetString("size", ctx),
                    Align = ParseAlignment(map, ctx, "align"),
                    Title = map.GetString("title", ctx),
                    Style = map.GetString("style", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'image' node", "type", "asset", "size", "align", "title", "style");
                return result;
            }
            case "break":
            {
                var result = new BreakNode { Style = map.GetString("style", ctx) };
                map.WarnUnmatchedFields(ctx, "'break' node", "type", "style");
                return result;
            }
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
            case "link":
            {
                var (stateAffecting, snapshotLabel) = map.GetBoolOrLabel("snapshot", ctx);
                var result = new LinkNode
                {
                    Label = map.GetRequiredString("label", ctx),
                    Style = map.GetString("style", ctx),
                    Target = map.GetString("target", ctx),
                    StateAffecting = stateAffecting,
                    SnapshotLabel = snapshotLabel,
                    OnClick = BuildNodeList(map.TryGet("onclick"), ctx, "'link' node onclick"),
                };
                map.WarnUnmatchedFields(ctx, "'link' node",
                    "type", "label", "style", "target", "snapshot", "onclick");
                return result;
            }
            case "popup":
            {
                var (stateAffecting, snapshotLabel) = map.GetBoolOrLabel("snapshot", ctx);
                var result = new PopupNode
                {
                    Label = map.GetString("label", ctx),
                    Style = map.GetString("style", ctx),
                    Layout = map.GetString("layout", ctx),
                    Header = BuildNodeList(map.TryGet("header"), ctx, "'popup' node header"),
                    Content = BuildNodeList(map.TryGet("content"), ctx, "'popup' node content"),
                    Okay = map.GetString("okay", ctx),
                    Cancel = map.GetString("cancel", ctx),
                    OnClose = BuildNodeList(map.TryGet("onclose"), ctx, "'popup' node onclose"),
                    Target = map.GetString("target", ctx),
                    StateAffecting = stateAffecting,
                    SnapshotLabel = snapshotLabel,
                };
                map.WarnUnmatchedFields(ctx, "'popup' node",
                    "type", "label", "style", "layout", "header", "content", "okay", "cancel", "onclose", "target", "snapshot");
                return result;
            }
            case "input":
            {
                var result = new InputNode
                {
                    Label = map.GetRequiredString("label", ctx),
                    Style = map.GetString("style", ctx),
                    Var = map.GetRequiredString("var", ctx),
                    Min = map.GetLong("min", ctx),
                    Max = map.GetLong("max", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'input' node", "type", "label", "style", "var", "min", "max");
                return result;
            }
            case "goto":
            {
                var result = new GotoNode
                {
                    Target = map.GetRequiredString("target", ctx),
                    SnapshotLabel = map.GetString("snapshot_label", ctx),
                };
                map.WarnUnmatchedFields(ctx, "'goto' node", "type", "target", "snapshot_label");
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
        // Flat form: if: + then: directly on the node, with an optional else: sibling — for a
        // single condition, this reads the same as the multi-branch form but without the
        // conditions: wrapper boilerplate.
        var flatIf = map.GetString("if", ctx);
        if (flatIf is not null)
        {
            var flatElseNode = map.TryGet("else");
            var flatResult = new ConditionalNode
            {
                Conditions = [new ConditionalBranch { If = flatIf, Then = BuildNodeList(map.TryGet("then"), ctx, "'conditional' node then") }],
                Else = flatElseNode is null ? null : BuildNodeList(flatElseNode, ctx, "'conditional' node else"),
            };
            map.WarnUnmatchedFields(ctx, "'conditional' node", "type", "if", "then", "else");
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
}
