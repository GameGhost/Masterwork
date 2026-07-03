using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

// Parses a single .mws.yaml passage file (MWS v0.3) into a MwsPassageDoc tree.
// Dispatches on each node's `type:` field against the low-level YAML representation model —
// see YamlNodeExtensions for why this bypasses YamlDotNet's generic object-graph deserializer.
public static class PassageYamlParser
{
    public static MwsPassageDoc ParsePassage(string yamlText)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(yamlText));
        if (stream.Documents.Count == 0)
            throw new FormatException("Empty YAML document");
        var root = (YamlMappingNode)stream.Documents[0].RootNode;

        V3Location? location = null;
        var locMap = root.GetMapping("location");
        if (locMap is not null)
            location = new V3Location { Name = locMap.GetString("name"), Icon = locMap.GetString("icon") };

        return new MwsPassageDoc
        {
            PassageId = root.GetRequiredString("passage_id"),
            Title = root.GetString("title"),
            Layout = root.GetRequiredString("layout"),
            Tags = root.GetStringList("tags"),
            Debug = root.GetBool("debug"),
            Location = location,
            CheckProgress = root.GetString("check_progress"),
            Nodes = BuildNodeList(root.TryGet("nodes")),
        };
    }

    internal static List<V3Node> BuildNodeList(YamlNode? node)
    {
        if (node is not YamlSequenceNode seq) return [];
        return seq.Children.Cast<YamlMappingNode>().Select(BuildNode).ToList();
    }

    internal static V3Node BuildNode(YamlMappingNode map)
    {
        var type = map.GetRequiredString("type");
        return type switch
        {
            "text" => new V3TextNode
            {
                Value = map.GetRequiredString("value"),
                Align = map.GetString("align"),
                Lets = map.GetStringList("lets"),
            },
            "image" => new V3ImageNode
            {
                Asset = map.GetRequiredString("asset"),
                Size = map.GetString("size"),
                Align = map.GetString("align"),
            },
            "break" => new V3BreakNode(),
            "paragraph_break" => new V3ParagraphBreakNode(),
            "section" => new V3SectionNode
            {
                Title = map.GetString("title"),
                Style = map.GetString("style"),
                Collapsed = map.GetBool("collapsed"),
                Content = BuildNodeList(map.TryGet("content")),
            },
            "let" => new V3LetNode
            {
                Var = map.GetRequiredString("var"),
                Expr = map.GetRequiredString("expr"),
            },
            "assign" => new V3AssignNode
            {
                Var = map.GetRequiredString("var"),
                Expr = map.GetRequiredString("expr"),
            },
            "navigation" => new V3NavigationNode
            {
                Label = map.GetRequiredString("label"),
                Style = map.GetString("style"),
                Target = map.GetRequiredString("target"),
                StateAffecting = map.GetBool("state_affecting", defaultValue: true),
                TimelineLabel = map.GetString("timeline_label"),
                Onclick = BuildNodeList(map.TryGet("onclick")),
            },
            "popup" => new V3PopupNode
            {
                Label = map.GetString("label"),
                Style = map.GetString("style"),
                Layout = map.GetString("layout"),
                Content = BuildNodeList(map.TryGet("content")),
                Onclose = map.GetString("onclose"),
                Button = map.GetString("button"),
                StateAffecting = map.GetBool("state_affecting"),
            },
            "input" => new V3InputNode
            {
                Label = map.GetRequiredString("label"),
                Style = map.GetString("style"),
                Text = map.GetRequiredString("text"),
                InputType = map.GetRequiredString("input"),
                Var = map.GetRequiredString("var"),
                Onsubmit = map.GetRequiredString("onsubmit"),
            },
            "prompt" => new V3PromptNode
            {
                Text = map.GetRequiredString("text"),
                InputType = map.GetRequiredString("input"),
                Var = map.GetRequiredString("var"),
            },
            "goto" => new V3GotoNode { Target = map.GetRequiredString("target") },
            "include_passage" => new V3IncludePassageNode { Target = map.GetRequiredString("target") },
            "conditional" => BuildConditional(map),
            "switch" => BuildSwitch(map),
            "foreach" => new V3ForeachNode
            {
                Var = map.GetRequiredString("var"),
                In = map.GetRequiredString("in"),
                Do = BuildNodeList(map.TryGet("do")),
            },
            "checkpoint" => new V3CheckpointNode
            {
                Id = map.GetRequiredString("id"),
                Display = map.GetString("display"),
                Diagnostic = map.GetString("diagnostic"),
            },
            "record" => new V3RecordNode { Id = map.GetRequiredString("id") },
            _ => new V3UnknownNode(type),
        };
    }

    private static V3ConditionalNode BuildConditional(YamlMappingNode map)
    {
        // Flat form: if: + then: directly on the node.
        var flatIf = map.GetString("if");
        if (flatIf is not null)
        {
            return new V3ConditionalNode
            {
                Conditions = [new V3ConditionalBranch { If = flatIf, Then = BuildNodeList(map.TryGet("then")) }],
                Else = null,
            };
        }

        // Multi-branch form: conditions: [{if, then}] + optional else:.
        var conditions = new List<V3ConditionalBranch>();
        if (map.GetSequence("conditions") is { } seq)
        {
            foreach (var branchNode in seq.Children.Cast<YamlMappingNode>())
            {
                conditions.Add(new V3ConditionalBranch
                {
                    If = branchNode.GetRequiredString("if"),
                    Then = BuildNodeList(branchNode.TryGet("then")),
                });
            }
        }
        var elseNode = map.TryGet("else");
        return new V3ConditionalNode
        {
            Conditions = conditions,
            Else = elseNode is null ? null : BuildNodeList(elseNode),
        };
    }

    private static V3SwitchNode BuildSwitch(YamlMappingNode map)
    {
        var cases = new List<V3SwitchCase>();
        if (map.GetSequence("cases") is { } seq)
        {
            foreach (var caseNode in seq.Children.Cast<YamlMappingNode>())
            {
                var matchNode = caseNode.TryGet("match")
                    ?? throw new FormatException("switch case missing 'match'");
                cases.Add(new V3SwitchCase
                {
                    Match = matchNode.ToNaturalValue(),
                    Nodes = BuildNodeList(caseNode.TryGet("nodes")),
                });
            }
        }
        var defaultNode = map.TryGet("default");
        return new V3SwitchNode
        {
            On = map.GetRequiredString("on"),
            Cases = cases,
            Default = defaultNode is null ? null : BuildNodeList(defaultNode),
        };
    }
}
