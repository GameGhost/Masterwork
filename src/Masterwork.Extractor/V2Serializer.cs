using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;

namespace Masterwork.Extractor;

/// <summary>
/// Carries optional source-location context into the serializer.
/// SourceRelativePath: relative path from the output dir to the .cs source file, e.g. "../file.cs".
/// PassageFileMap: maps passage IDs to relative YAML filenames, e.g. "./00042-Name.mws.yaml".
/// </summary>
public record SerializationContext(
    string? SourceRelativePath,
    IReadOnlyDictionary<string, string>? PassageFileMap
);

/// <summary>
/// Converts a MwsPassage (v0.1 intermediate representation produced by the extractor)
/// to a v0.2 Dictionary suitable for YAML serialization.
///
/// This keeps all v0.1 MwsNode subclasses as the internal extraction representation
/// (so tests and the visitor are unchanged) while producing v0.2-format YAML output.
/// </summary>
public static partial class V2Serializer
{
    public static Dictionary<string, object?> ToDict(MwsPassage passage, SerializationContext? ctx = null)
    {
        // Scan top-level nodes for header fields to hoist
        string? locationName = null, locationIcon = null, checkProgress = null;
        var bodyNodes = HoistHeaderNodes(passage.Nodes, ref locationName, ref locationIcon, ref checkProgress);

        var d = new Dictionary<string, object?>
        {
            ["format"] = "mws/0.2",
            ["passage_id"] = passage.PassageId,
        };
        if (!string.IsNullOrEmpty(passage.Title) && passage.Title != passage.PassageId)
            d["title"] = passage.Title;
        if (passage.Tags.Length > 0) d["tags"] = passage.Tags;
        d["layout"] = passage.Layout;
        if (passage.Debug) d["debug"] = true;

        if (locationName is not null || locationIcon is not null)
        {
            var loc = new Dictionary<string, object?>();
            if (locationName is not null) loc["name"] = locationName;
            if (locationIcon is not null) loc["icon"] = locationIcon;
            d["location"] = loc;
        }
        if (checkProgress is not null) d["check_progress"] = checkProgress;

        d["nodes"] = TransformNodeList(bodyNodes, ctx);
        return d;
    }

    // ── Header node extraction ─────────────────────────────────────────────

    private static List<MwsNode> HoistHeaderNodes(
        List<MwsNode> nodes,
        ref string? locationName, ref string? locationIcon, ref string? checkProgress)
    {
        var body = new List<MwsNode>(nodes.Count);
        foreach (var node in nodes)
        {
            switch (node)
            {
                case SetLocationNode loc:
                    locationName ??= loc.Name;
                    locationIcon ??= loc.Icon;
                    break;
                case CheckProgressNode cp:
                    checkProgress ??= cp.TargetPassage;
                    break;
                default:
                    body.Add(node);
                    break;
            }
        }
        return body;
    }

    // ── Node list transformation ───────────────────────────────────────────

    public static List<Dictionary<string, object?>> TransformNodeList(List<MwsNode> nodes, SerializationContext? ctx = null)
    {
        var result = new List<Dictionary<string, object?>>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            // Pair SectionHeadingNode + optional following SectionBodyNode → section
            // Skip over break/paragraph_break nodes between hubTitle and hubDetails scopes.
            if (node is SectionHeadingNode heading)
            {
                List<MwsNode> bodyNodes = [];
                var j = i + 1;
                while (j < nodes.Count && nodes[j] is BreakNode or ParagraphBreakNode)
                    j++;
                if (j < nodes.Count && nodes[j] is SectionBodyNode body)
                {
                    bodyNodes = body.Nodes;
                    i = j;
                }
                AddSrcSentinel(result, heading.SourceLine, ctx);
                result.Add(TransformSection(heading.Text, bodyNodes, headingSourceLine: heading.SourceLine, ctx: ctx));
                continue;
            }
            if (node is SectionBodyNode orphanBody)
            {
                AddSrcSentinel(result, orphanBody.SourceLine, ctx);
                result.Add(TransformSection(null, orphanBody.Nodes, headingSourceLine: orphanBody.SourceLine, ctx: ctx));
                continue;
            }

            // GotoMenuNode — app navigation is not a module concern; drop
            if (node is GotoMenuNode) continue;
            // SetLocationNode / CheckProgressNode — should have been hoisted; ignore
            if (node is SetLocationNode || node is CheckProgressNode) continue;

            var dicts = TransformNode(node, ctx).ToList();
            if (dicts.Count > 0)
                AddSrcSentinel(result, node.SourceLine, ctx);
            foreach (var d in dicts)
                result.Add(d);
        }
        return result;
    }

    // Inserts a _src sentinel dict before a node when source location is available.
    // InjectSentinelComments in Program.cs converts these to "# path:line" YAML comments.
    private static void AddSrcSentinel(List<Dictionary<string, object?>> result, int? sourceLine, SerializationContext? ctx)
    {
        if (sourceLine.HasValue && ctx?.SourceRelativePath is not null)
            result.Add(new() { ["_src"] = $"{ctx.SourceRelativePath}:{sourceLine.Value}" });
    }

    // Appends a _link field to a node dict immediately after the "target" key was inserted.
    // InjectSentinelComments converts this to an inline "# file" comment on the target line.
    private static void AddLinkHint(Dictionary<string, object?> d, string target, SerializationContext? ctx)
    {
        if (ctx?.PassageFileMap?.TryGetValue(target, out var file) == true && file is not null)
            d["_link"] = file;
    }

    // Returns one or more v0.2 dicts for a single v0.1 node.
    private static IEnumerable<Dictionary<string, object?>> TransformNode(MwsNode node, SerializationContext? ctx = null)
    {
        switch (node)
        {
            case TextNode text:
                yield return TransformText(text);
                break;
            case LetNode let:
                foreach (var d in TransformLet(let, ctx))
                    yield return d;
                break;
            case EffectNode effect:
                foreach (var d in TransformEffect(effect, ctx))
                    yield return d;
                break;
            case LinkNode link:
                yield return TransformNavigation(link, ctx);
                break;
            case ExpandLinkNode expand:
                yield return TransformPopup(expand, ctx);
                break;
            case InputPromptNode input:
                yield return TransformInputAction(input);
                break;
            case GotoNode go:
                yield return TransformGoto(go, ctx);
                break;
            case SetupBlockNode setup:
                yield return TransformSection(null, setup.Nodes, "setup", setup.SourceLine, ctx);
                break;
            case ConditionalNode cond:
                yield return TransformConditional(cond, ctx);
                break;
            case SwitchNode sw:
                yield return TransformSwitch(sw, ctx);
                break;
            case ForeachNode fe:
                yield return TransformForeach(fe, ctx);
                break;
            case IncludePassageNode inc:
            {
                var incD = new Dictionary<string, object?> { ["type"] = "include_passage", ["target"] = inc.Target };
                AddLinkHint(incD, inc.Target, ctx);
                yield return incD;
                break;
            }
            case BreakNode:
                yield return new() { ["type"] = "break" };
                break;
            case ParagraphBreakNode:
                yield return new() { ["type"] = "paragraph_break" };
                break;
            case CommentedBreakNode cb:
                yield return new() { ["_commented_break"] = cb.IsParagraph ? "paragraph_break" : "break" };
                break;
            case CheckpointNode cp:
                yield return TransformCheckpoint(cp);
                break;
            case EndOfGenerationNode eog:
                foreach (var d in TransformEndOfGeneration(eog))
                    yield return d;
                break;
            case ModalNode modal:
                foreach (var d in TransformModal(modal, ctx))
                    yield return d;
                break;
            case SetupNotificationNode sn:
                foreach (var d in TransformSetupNotification(sn, ctx))
                    yield return d;
                break;
            case UnknownNode unk:
                var ud = new Dictionary<string, object?> { ["type"] = "unknown", ["original_code"] = unk.OriginalCode };
                if (unk.Note is not null) ud["note"] = unk.Note;
                yield return ud;
                break;
            default:
                yield return new() { ["type"] = "unknown", ["original_code"] = node.GetType().Name };
                break;
        }
    }

    // ── Text ──────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformText(TextNode text)
    {
        var d = new Dictionary<string, object?> { ["type"] = "text" };

        string value;
        if (text.Template is not null)
        {
            value = ApplyInlineStyle(text.Template, text.Style);
        }
        else if (text.Runs.Count > 0)
        {
            value = BuildValueFromRuns(text.Runs);
        }
        else
        {
            d["value"] = "";
            return d;
        }

        d["value"] = value;
        if (text.Lets is { Count: > 0 }) d["lets"] = text.Lets;
        return d;
    }

    private static string ApplyInlineStyle(string template, string? style) => style switch
    {
        "bold" => $"**{template}**",
        "italic" => $"_{template}_",
        _ => template,
    };

    private static string BuildValueFromRuns(List<TextRun> runs)
    {
        var sb = new StringBuilder();
        bool inBold = false, inItalic = false;

        foreach (var run in runs)
        {
            if (run.AssetRef is not null)
            {
                if (inBold) { sb.Append("**"); inBold = false; }
                if (inItalic) { sb.Append("_"); inItalic = false; }
                var slug = run.AssetRef.StartsWith("icon://") ? run.AssetRef["icon://".Length..] : run.AssetRef;
                sb.Append($"{{icon:{slug}}}");
                continue;
            }
            if (run.Text is null) continue;

            bool needBold = run.Style == "bold";
            bool needItalic = run.Style == "italic";

            if (inBold && !needBold) { sb.Append("**"); inBold = false; }
            if (inItalic && !needItalic) { sb.Append("_"); inItalic = false; }
            if (!inBold && needBold) { sb.Append("**"); inBold = true; }
            if (!inItalic && needItalic) { sb.Append("_"); inItalic = true; }

            sb.Append(run.Text);
        }

        if (inBold) sb.Append("**");
        if (inItalic) sb.Append("_");

        return sb.ToString();
    }

    // ── Let ───────────────────────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformLet(LetNode let, SerializationContext? ctx = null)
    {
        // Pop/dequeue produce TWO output nodes: let + assign
        if (let.Pop is not null)
        {
            yield return MakeLet(let.Var, $"{let.Pop}[^1]");
            yield return MakeAssign(let.Pop, $"{let.Pop}[..^1]");
            yield break;
        }
        if (let.Dequeue is not null)
        {
            yield return MakeLet(let.Var, $"{let.Dequeue}[0]");
            yield return MakeAssign(let.Dequeue, $"{let.Dequeue}[1..]");
            yield break;
        }

        var d = MakeLet(let.Var, LetToExpr(let));
        yield return d;
    }

    private static string LetToExpr(LetNode let)
    {
        if (let.Random is not null) return VarRandomToExpr(let.Random);
        if (let.Replace is not null) return ReplaceToExpr(let.Replace);
        if (let.PickFrom is not null) return $"{let.PickFrom}.shuffled(\"{let.Var}_0\")[0]";
        if (let.Array is not null)
            return "[" + string.Join(", ", let.Array.Select(v => $"{v}")) + "]";
        if (let.Compute is not null) return let.Compute;
        if (let.Sort is not null) return SortToExpr(let.Sort.From ?? let.Var, let.Sort);
        return "null";
    }

    private static string VarRandomToExpr(VarRandom vr)
    {
        var key = vr.SeedKey is not null ? $"\"{EscapeStr(vr.SeedKey)}\"" : "\"?\"";
        return vr.RandomType switch
        {
            "choose-one" => vr.Values.Count == 1
                ? ValueToExpr(vr.Values[0])
                : $"[{ValuesToExprList(vr.Values)}].shuffled({key})[0]",
            "range" => $"rand_between({vr.Min}, {vr.Max}, {key})",
            "rand-between" => $"rand_between({vr.Min}, {vr.Max}, {key})",
            "shuffled_array" => $"[{ValuesToExprList(vr.Values)}].shuffled({key})",
            _ => $"/* unsupported random type: {vr.RandomType} */",
        };
    }

    private static string ValuesToExprList(List<object> values) =>
        string.Join(", ", values.Select(ValueToExpr));

    private static string ValueToExpr(object v) => v switch
    {
        int n => n.ToString(),
        long l => l.ToString(),
        bool b => b ? "true" : "false",
        string s => StringValueToExpr(s),
        _ => $"\"{v}\"",
    };

    private static string StringValueToExpr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        // restext:// URI — wrap as string literal
        if (s.StartsWith("restext://")) return $"\"{s}\"";
        // Single var ref: {varName} → varName
        if (s.StartsWith("{") && s.EndsWith("}") && !s[1..^1].Contains('{'))
        {
            var inner = s[1..^1].Replace(".first()", "[0]");
            return inner;
        }
        // Already-expression forms: ternary, global, input
        if (s.StartsWith("(") || s.Contains("global:") || s.Contains("input:"))
            return s;
        // Unhandled fallback marker
        if (s.StartsWith("?(")) return s;
        // Plain string (no braces, not an expression)
        if (!s.Contains('{') && !s.Contains('+'))
            return $"\"{EscapeStr(s)}\"";
        // Template/expression with {var} refs — keep as-is
        return s;
    }

    private static string ReplaceToExpr(VarReplace r)
    {
        var src = r.Source;
        var with = $"\"{EscapeStr(r.With)}\"";
        if (r.Find is List<string> finds)
        {
            // Chained replaces
            var result = src;
            foreach (var find in finds)
                result = $"{result}.replace(\"{EscapeStr(find)}\", {with})";
            return result;
        }
        var findStr = r.Find?.ToString() ?? "";
        return $"{src}.replace(\"{EscapeStr(findStr)}\", {with})";
    }

    private static string SortToExpr(string source, SortSpec sort)
    {
        var dir = $"\"{EscapeStr(sort.Direction)}\"";
        if (sort.Property is not null)
            return $"{source}.toSorted({dir}, \"{EscapeStr(sort.Property)}\")";
        return $"{source}.toSorted({dir})";
    }

    // ── Effect → assign nodes ─────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformEffect(EffectNode effect, SerializationContext? ctx = null)
    {
        if (effect.VarSets is not null)
        {
            foreach (var (varName, val) in effect.VarSets)
                yield return MakeAssign(varName, VarSetValueToExpr(varName, val), ctx);
        }
        if (effect.VarMath is not null)
        {
            foreach (var (varName, math) in effect.VarMath)
                yield return MakeAssign(varName, VarMathToExpr(varName, math));
        }
        if (effect.VarRandom is not null)
        {
            foreach (var (varName, vr) in effect.VarRandom)
                yield return MakeAssign(varName, VarRandomToExpr(vr));
        }
        if (effect.VarPush is not null)
        {
            foreach (var (varName, valExpr) in effect.VarPush)
            {
                var val = StringValueToExpr(valExpr);
                yield return MakeAssign(varName, $"[..{varName}, {val}]");
            }
        }
        if (effect.VarPop is not null)
            yield return MakeAssign(effect.VarPop, $"{effect.VarPop}[..^1]");
        if (effect.VarSort is not null)
        {
            foreach (var (varName, sort) in effect.VarSort)
                yield return MakeAssign(varName, SortToExpr(varName, sort));
        }
        if (effect.VarRemove is not null)
        {
            foreach (var (varName, val) in effect.VarRemove)
            {
                var valExpr = StringValueToExpr(val);
                yield return MakeAssign(varName, $"{varName}.except({valExpr})");
            }
        }
    }

    private static string VarSetValueToExpr(string varName, object? val)
    {
        return val switch
        {
            null => "null",
            int n => n.ToString(),
            long l => l.ToString(),
            bool b => b ? "true" : "false",
            List<object> list => "[" + string.Join(", ", list.Select(v => ValueToExpr(v))) + "]",
            string s => VarSetStringToExpr(s),
            _ => $"\"{val}\"",
        };
    }

    private static string VarSetStringToExpr(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        // restext:// URI
        if (s.StartsWith("restext://")) return $"\"{s}\"";
        // Single brace-wrapped expression: {expr} → expr (strips outer braces)
        if (s.StartsWith("{") && s.EndsWith("}") && !s[1..^1].Contains('{'))
        {
            var inner = s[1..^1].Replace(".first()", "[0]");
            return inner;
        }
        // Already-expression forms
        if (s.StartsWith("(") || s.Contains("global:") || s.Contains("input:"))
            return s;
        if (s.StartsWith("?(")) return s;
        // Method calls on vars that came out as bare string ("arr.shuffle()" etc.)
        if (s.Contains(".shuffle()")) return s;
        // Sum of multiple var refs came out as "a + b + c" style (no braces needed)
        // Plain string with no brace references
        if (!s.Contains('{'))
            return $"\"{EscapeStr(s)}\"";
        // Template / expression with embedded {var} refs — keep as-is
        return s;
    }

    private static string VarMathToExpr(string varName, string math)
    {
        if (math == "+0") return varName;
        // Full expression form produced by TryBuildArithExpr (e.g. "= x + y * 2")
        if (math.StartsWith("= ")) return math[2..];
        var op = math[0];
        var operand = math[1..];
        // Strip {Y} → Y for var operands
        if (operand.StartsWith("{") && operand.EndsWith("}"))
            operand = operand[1..^1];
        return $"{varName} {op} {operand}";
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformNavigation(LinkNode link, SerializationContext? ctx = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = "navigation",
            ["label"] = link.Label,
            ["target"] = link.Target,
        };
        AddLinkHint(d, link.Target, ctx);
        d["state_affecting"] = link.StateAffecting;
        if (link.TimelineLabel is not null) d["timeline_label"] = link.TimelineLabel;
        if (link.Nodes.Count > 0) d["nodes"] = TransformNodeList(link.Nodes, ctx);
        return d;
    }

    // ── Popup ─────────────────────────────────────────────────────────────

    // Matches: ViewBiddingSystem.instance.OnShowBidding("PassageId", BiddingSystem.Voting/Bidding)
    [GeneratedRegex(@"ViewBiddingSystem\.instance\.OnShowBidding\(""([^""]+)"",\s*BiddingSystem\.(\w+)\)")]
    private static partial Regex BiddingCallPattern();

    private static Dictionary<string, object?> TransformPopup(ExpandLinkNode expand, SerializationContext? ctx = null)
    {
        // Scan children for a ViewBiddingSystem.OnShowBidding call.
        // If present: hoist chrome + onclose to the popup dict; remove the unknown node.
        string? chrome = null, onclose = null;
        var childNodes = new List<MwsNode>(expand.ExpandNodes.Count);
        foreach (var child in expand.ExpandNodes)
        {
            if (child is UnknownNode unk)
            {
                var m = BiddingCallPattern().Match(unk.OriginalCode ?? "");
                if (m.Success)
                {
                    onclose = m.Groups[1].Value;
                    chrome = m.Groups[2].Value.ToLowerInvariant(); // "Voting" → "voting"
                    continue;
                }
            }
            childNodes.Add(child);
        }

        var d = new Dictionary<string, object?>
        {
            ["type"] = "popup",
        };
        if (chrome is not null) d["chrome"] = chrome;
        d["label"] = expand.Label;
        d["state_affecting"] = expand.StateAffecting;
        if (onclose is not null)
        {
            d["onclose"] = onclose;
            AddLinkHint(d, onclose, ctx);
        }

        var transformed = TransformNodeList(childNodes, ctx);
        if (transformed.Count > 0) d["nodes"] = transformed;
        return d;
    }

    // ── Input action ──────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformInputAction(InputPromptNode input)
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = "input",
            ["label"] = input.PromptId,
            ["text"] = input.Text,
            ["input_type"] = input.InputType,
            ["store_in"] = input.StoreIn,
        };
        if (input.ResumePassage is not null) d["onsubmit"] = input.ResumePassage;
        return d;
    }

    // ── Goto ──────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformGoto(GotoNode go, SerializationContext? ctx = null)
    {
        var d = new Dictionary<string, object?> { ["type"] = "goto", ["target"] = go.Target };
        AddLinkHint(d, go.Target, ctx);
        return d;
    }

    // ── Section ───────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformSection(
        string? title, List<MwsNode> innerNodes, string? style = null, int? headingSourceLine = null, SerializationContext? ctx = null)
    {
        var d = new Dictionary<string, object?> { ["type"] = "section" };
        if (title is not null) d["title"] = title;
        if (style is not null) d["style"] = style;
        d["nodes"] = TransformNodeList(innerNodes, ctx);
        return d;
    }

    // ── Conditional ───────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformConditional(ConditionalNode cond, SerializationContext? ctx = null)
    {
        return new()
        {
            ["type"] = "conditional",
            ["branches"] = cond.Branches.Select(b =>
            {
                var bd = new Dictionary<string, object?>();
                if (b.Condition is not null) bd["condition"] = b.Condition;
                if (b.Else == true) bd["else"] = true;
                bd["nodes"] = TransformNodeList(b.Nodes, ctx);
                return bd;
            }).ToList(),
        };
    }

    // ── Switch ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformSwitch(SwitchNode sw, SerializationContext? ctx = null)
    {
        return new()
        {
            ["type"] = "switch",
            ["on"] = sw.On,
            ["cases"] = sw.Cases.Select(c =>
            {
                var cd = new Dictionary<string, object?>();
                if (c.Match is not null) cd["match"] = c.Match;
                if (c.Default == true) cd["default"] = true;
                cd["nodes"] = TransformNodeList(c.Nodes, ctx);
                return cd;
            }).ToList(),
        };
    }

    // ── Foreach ───────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformForeach(ForeachNode fe, SerializationContext? ctx = null)
    {
        return new()
        {
            ["type"] = "foreach",
            ["var"] = fe.Var,
            ["in"] = fe.In,
            ["nodes"] = TransformNodeList(fe.Nodes, ctx),
        };
    }

    // ── Checkpoint ────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformCheckpoint(CheckpointNode cp)
    {
        var d = new Dictionary<string, object?> { ["type"] = "checkpoint", ["id"] = cp.Id };
        if (cp.DisplayLabel is not null) d["display_label"] = cp.DisplayLabel;
        if (cp.DiagnosticLabel is not null) d["diagnostic_label"] = cp.DiagnosticLabel;
        return d;
    }

    // ── EndOfGeneration ───────────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformEndOfGeneration(EndOfGenerationNode eog)
    {
        // Transform to section+checkpoint pattern
        if (eog.Message is not null)
        {
            yield return new()
            {
                ["type"] = "section",
                ["style"] = "panel",
                ["nodes"] = new List<Dictionary<string, object?>>
                {
                    new() { ["type"] = "text", ["value"] = eog.Message }
                },
            };
        }
        yield return new()
        {
            ["type"] = "checkpoint",
            ["id"] = $"generation_{eog.Generation}_complete",
            ["display_label"] = $"Generation {eog.Generation}",
        };
    }

    // ── Modal ─────────────────────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformModal(ModalNode modal, SerializationContext? ctx = null)
    {
        // Transform end-of-round modal to section+checkpoint+navigation pattern
        var nodes = new List<Dictionary<string, object?>>();

        if (modal.Body is not null)
            nodes.Add(new() { ["type"] = "text", ["value"] = modal.Body });

        if (modal.Round.HasValue)
        {
            yield return new()
            {
                ["type"] = "section",
                ["style"] = "panel",
                ["nodes"] = nodes,
            };
            yield return new()
            {
                ["type"] = "checkpoint",
                ["id"] = $"round_{modal.Round}_complete",
                ["display_label"] = $"Round {modal.Round}",
            };
        }
        else if (nodes.Count > 0)
        {
            yield return new() { ["type"] = "section", ["style"] = "panel", ["nodes"] = nodes };
        }

        if (modal.Next is not null)
        {
            var label = modal.Instruction ?? "Continue";
            var navD = new Dictionary<string, object?>
            {
                ["type"] = "navigation",
                ["label"] = label,
                ["target"] = modal.Next,
            };
            AddLinkHint(navD, modal.Next, ctx);
            navD["state_affecting"] = true;
            yield return navD;
        }
    }

    // ── SetupNotification ─────────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformSetupNotification(SetupNotificationNode sn, SerializationContext? ctx = null)
    {
        var nodes = new List<Dictionary<string, object?>>();
        if (sn.Title is not null)
            nodes.Add(new() { ["type"] = "text", ["value"] = $"**{sn.Title}**" });
        if (sn.Text is not null)
            nodes.Add(new() { ["type"] = "text", ["value"] = sn.Text });

        yield return new() { ["type"] = "section", ["style"] = "panel", ["nodes"] = nodes };

        if (sn.NextPassage is not null)
        {
            var navD = new Dictionary<string, object?>
            {
                ["type"] = "navigation",
                ["label"] = "Continue",
                ["target"] = sn.NextPassage,
            };
            AddLinkHint(navD, sn.NextPassage, ctx);
            navD["state_affecting"] = true;
            yield return navD;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Dictionary<string, object?> MakeLet(string varName, string expr,
        List<string>? lets = null)
    {
        var d = new Dictionary<string, object?> { ["type"] = "let", ["var"] = varName, ["expr"] = expr };
        if (lets is { Count: > 0 }) d["lets"] = lets;
        return d;
    }

    private static Dictionary<string, object?> MakeAssign(string varName, string expr, SerializationContext? ctx = null)
    {
        var d = new Dictionary<string, object?> { ["type"] = "assign", ["var"] = varName, ["expr"] = expr };
        // Annotate when expr is a simple quoted passage name: "PassageId"
        if (ctx?.PassageFileMap is not null
            && expr.Length > 2 && expr[0] == '"' && expr[^1] == '"'
            && ctx.PassageFileMap.TryGetValue(expr[1..^1], out var passageFile))
        {
            d["_link"] = passageFile;
        }
        return d;
    }

    private static string EscapeStr(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
