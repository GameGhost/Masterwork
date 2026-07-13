using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;

namespace Masterwork.Extractor;

/// <summary>
/// Carries optional source-location context into the serializer.
/// SourceRelativePath: relative path from the output dir to the .cs source file, e.g. "../file.cs".
/// PassageFileMap: maps passage IDs to relative YAML filenames, e.g. "./00042-Name.mws.yaml".
/// Variables: the module's discovered-variables dictionary (mutable, shared with the caller) —
/// <see cref="V2Serializer.TransformInputAction"/> registers a synthetic guard variable into it
/// while serializing an <c>OnGenerationBtn</c>-derived input popup (see its remarks).
/// </summary>
public record SerializationContext(
    string? SourceRelativePath,
    IReadOnlyDictionary<string, string>? PassageFileMap,
    Dictionary<string, VarDef>? Variables = null
);

/// <summary>
/// Serializes a MwsPassage to a v0.3 Dictionary suitable for YAML emission.
///
/// Most node types delegate to their own ToDict() which already produce v0.3 output.
/// This class handles the remaining output concerns:
///   • Header node hoisting (SetLocationNode → location header, CheckProgressNode)
///   • Source sentinel injection (_src comments)
///   • Passage-file link hints (_link inline comments)
///   • Multi-node expansions: EffectNode → assign nodes, LetNode.Pop/Dequeue → 2 nodes
///   • Complex structural transforms: ExpandLinkNode → popup/navigation,
///     SectionHeadingNode+SectionBodyNode → section, ModalNode, EndOfGenerationNode, etc.
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
            ["format"] = "mws/0.4",
            ["passage_id"] = passage.PassageId,
        };
        if (!string.IsNullOrEmpty(passage.Title) && passage.Title != passage.PassageId)
        {
            d["title"] = passage.Title;
        }

        if (!string.IsNullOrEmpty(passage.Subtitle))
        {
            d["subtitle"] = passage.Subtitle;
        }

        if (passage.Tags.Length > 0)
        {
            d["tags"] = passage.Tags;
        }

        d["layout"] = passage.Layout;
        if (passage.Debug)
        {
            d["debug"] = true;
        }

        if (locationName is not null || locationIcon is not null)
        {
            var loc = new Dictionary<string, object?>();
            if (locationName is not null)
            {
                loc["name"] = locationName;
            }

            if (locationIcon is not null)
            {
                loc["icon"] = locationIcon;
            }

            d["location"] = loc;
        }
        if (checkProgress is not null)
        {
            d["check_progress"] = checkProgress;
        }

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
                {
                    j++;
                }

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

            // Pair a standalone ViewItemObtain.SetupPassagename assignment (SetupNotificationNode)
            // with the styleScope("setupStyleEvnt", ...) block immediately following it at the top
            // level of a passage. Cradle sets SetupPassagename right before that block to configure
            // where the block's own popup navigates to on Accept — it isn't a standalone
            // notification. (When the same pair instead appears inside a link(...)'s callback
            // fragment, FragmentStitchPass folds them into an ExpandLinkNode and TransformPopup
            // already merges them correctly; this handles the top-level case that bypasses that path.)
            if (node is SetupNotificationNode standaloneSn)
            {
                var j = i + 1;
                while (j < nodes.Count && nodes[j] is BreakNode or ParagraphBreakNode)
                {
                    j++;
                }

                if (j < nodes.Count && nodes[j] is SetupBlockNode pairedBlock)
                {
                    AddSrcSentinel(result, standaloneSn.SourceLine, ctx);
                    result.Add(TransformSetupNotificationBlock(standaloneSn, pairedBlock, ctx));
                    i = j;
                    continue;
                }
            }

            // GotoMenuNode — app navigation is not a module concern; drop
            if (node is GotoMenuNode)
            {
                continue;
            }
            // SetLocationNode / CheckProgressNode — should have been hoisted; ignore
            if (node is SetLocationNode || node is CheckProgressNode)
            {
                continue;
            }

            var dicts = TransformNode(node, ctx).ToList();
            if (dicts.Count > 0)
            {
                AddSrcSentinel(result, node.SourceLine, ctx);
            }

            foreach (var d in dicts)
            {
                result.Add(d);
            }
        }
        return result;
    }

    // Inserts a _src sentinel dict before a node when source location is available.
    // InjectSentinelComments in Program.cs converts these to "# path:line" YAML comments.
    private static void AddSrcSentinel(List<Dictionary<string, object?>> result, int? sourceLine, SerializationContext? ctx)
    {
        if (sourceLine.HasValue && ctx?.SourceRelativePath is not null)
        {
            result.Add(new() { ["_src"] = $"{ctx.SourceRelativePath}:{sourceLine.Value}" });
        }
    }

    // Appends a _link field to a node dict immediately after the "target" key was inserted.
    // InjectSentinelComments converts this to an inline "# file" comment on the target line.
    // Skipped for expression-valued targets (starting with "${") since the passage is not statically known.
    private static void AddLinkHint(Dictionary<string, object?> d, string target, SerializationContext? ctx)
    {
        if (target.StartsWith("${", StringComparison.Ordinal))
        {
            return;
        }

        if (ctx?.PassageFileMap?.TryGetValue(target, out var file) == true && file is not null)
        {
            d["_link"] = file;
        }
    }

    // Returns one or more v0.2 dicts for a single v0.1 node.
    private static IEnumerable<Dictionary<string, object?>> TransformNode(MwsNode node, SerializationContext? ctx = null)
    {
        switch (node)
        {
            case TextNode text:
                yield return text.ToDict();
                break;
            case ImageNode img:
                yield return TransformImage(img);
                break;
            case LetNode let:
                foreach (var d in TransformLetNode(let, ctx))
                {
                    yield return d;
                }

                break;
            case EffectNode effect:
                foreach (var d in TransformEffect(effect, ctx))
                {
                    yield return d;
                }

                break;
            case LinkNode link:
                yield return TransformNavigation(link, ctx);
                break;
            case ExpandLinkNode expand:
                yield return IsNavigationOnly(expand.ExpandNodes)
                    ? BuildNavigationFromExpand(expand, ctx)
                    : TransformPopup(expand, ctx);
                break;
            case InputPromptNode input:
                yield return TransformInputAction(input, ctx);
                break;
            case GotoNode go:
                yield return TransformGoto(go, ctx);
                break;
            case SetupBlockNode setup:
            {
                // Standalone setupStyle in a main passage → auto-display popup (no label, no target).
                // Inside an expand-link fragment, SetupBlockNode is handled by TransformPopup instead.
                // Still needs an Okay button — this popup style always has an acknowledgement button
                // in the original app, even with no destination passage (okay/target/onclose are all
                // independently optional, so the popup just closes in place with no engine round-trip).
                var pd = new Dictionary<string, object?> { ["type"] = "popup", ["layout"] = "setup", ["okay"] = "Accept" };
                var (setupHeaderNodes, setupContentNodes) = SplitPopupHeaderNodes(setup.Nodes);
                var sHeader = TransformNodeList(setupHeaderNodes, ctx);
                if (sHeader.Count > 0)
                    {
                        pd["header"] = sHeader;
                    }

                    var sNodes = TransformNodeList(setupContentNodes, ctx);
                if (sNodes.Count > 0)
                    {
                        pd["content"] = sNodes;
                    }

                    yield return pd;
                break;
            }
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
                yield return new() { ["type"] = "break", ["style"] = "paragraph" };
                break;
            case CommentedBreakNode cb:
                yield return new() { ["_commented_break"] = cb.IsParagraph ? "paragraph_break" : "break" };
                break;
            case CheckpointNode cp:
                yield return TransformCheckpoint(cp);
                break;
            case EndOfGenerationNode eog:
                yield return TransformEndOfGeneration(eog);
                break;
            case ModalNode modal:
                foreach (var d in TransformModal(modal, ctx))
                {
                    yield return d;
                }

                break;
            case SetupNotificationNode sn:
                foreach (var d in TransformSetupNotification(sn, ctx))
                {
                    yield return d;
                }

                break;
            case UnknownNode unk:
                var ud = new Dictionary<string, object?> { ["type"] = "unknown", ["original_code"] = unk.OriginalCode };
                if (unk.Note is not null)
                {
                    ud["note"] = unk.Note;
                }

                yield return ud;
                break;
            default:
                yield return new() { ["type"] = "unknown", ["original_code"] = node.GetType().Name };
                break;
        }
    }

    // ── Text ──────────────────────────────────────────────────────────────
    // TextNode.ToDict() now produces v0.2 `value:` directly — no transformation needed.

    private static Dictionary<string, object?> TransformImage(ImageNode img)
    {
        var d = new Dictionary<string, object?> { ["type"] = "image", ["asset"] = img.AssetRef };
        if (img.Size is not null)
        {
            d["size"] = img.Size;
        }

        if (img.Align is not null)
        {
            d["align"] = img.Align;
        }

        if (img.Style is not null)
        {
            d["style"] = img.Style;
        }

        return d;
    }

    // ── Let ───────────────────────────────────────────────────────────────
    // LetNode.ToDict() now produces v0.2 `expr:` directly.
    // Pop/Dequeue still require two output nodes, so they are expanded here.

    private static IEnumerable<Dictionary<string, object?>> TransformLetNode(LetNode let, SerializationContext? ctx = null)
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

        yield return let.ToDict();
    }

    // ── Effect → assign nodes ─────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformEffect(EffectNode effect, SerializationContext? ctx = null)
    {
        if (effect.VarSets is not null)
        {
            foreach (var (varName, val) in effect.VarSets)
            {
                yield return MakeAssign(varName, MwsExprHelper.VarSetValueToExpr(val), ctx);
            }
        }
        if (effect.VarMath is not null)
        {
            foreach (var (varName, math) in effect.VarMath)
            {
                yield return MakeAssign(varName, MwsExprHelper.VarMathToExpr(varName, math));
            }
        }
        if (effect.VarRandom is not null)
        {
            foreach (var (varName, vr) in effect.VarRandom)
            {
                // shuffled_array with no literal values = reshuffle an existing variable in-place.
                var expr = vr is { RandomType: "shuffled_array", Values.Count: 0 }
                    ? $"{varName}.shuffled(\"{MwsExprHelper.EscapeStr(vr.SeedKey ?? "?")}\")"
                    : MwsExprHelper.VarRandomToExpr(vr);
                yield return MakeAssign(varName, expr);
            }
        }
        if (effect.VarPush is not null)
        {
            foreach (var (varName, valExpr) in effect.VarPush)
            {
                var val = MwsExprHelper.StringValueToExpr(valExpr);
                yield return MakeAssign(varName, $"[..{varName}, {val}]");
            }
        }
        if (effect.VarPop is not null)
        {
            yield return MakeAssign(effect.VarPop, $"{effect.VarPop}[..^1]");
        }

        if (effect.VarSort is not null)
        {
            foreach (var (varName, sort) in effect.VarSort)
            {
                yield return MakeAssign(varName, MwsExprHelper.SortToExpr(varName, sort));
            }
        }
        if (effect.VarRemove is not null)
        {
            foreach (var (varName, val) in effect.VarRemove)
            {
                var valExpr = MwsExprHelper.StringValueToExpr(val);
                yield return MakeAssign(varName, $"{varName}.except({valExpr})");
            }
        }
    }

    // ── Navigation ────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformNavigation(LinkNode link, SerializationContext? ctx = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = "link",
            ["label"] = link.Label,
            ["target"] = link.Target,
        };
        AddLinkHint(d, link.Target, ctx);
        // Unified field: a string implies snapshot=true and doubles as the label, so only emit the
        // separate bool when there's no label to fold it into.
        d["snapshot"] = link.TimelineLabel is not null ? link.TimelineLabel : link.StateAffecting;

        if (link.Nodes.Count > 0)
        {
            d["onclick"] = TransformNodeList(link.Nodes, ctx);
        }

        return d;
    }

    // ── Navigation-only expand-link conversion ─────────────────────────────

    // Returns true when every node is a GotoNode or a ConditionalNode whose branches are also navigation-only.
    // Used to convert an expand-link that only navigates into proper navigation node(s).
    private static bool IsNavigationOnly(List<MwsNode> nodes) =>
        nodes.Count > 0 && nodes.All(n => n switch
        {
            GotoNode => true,
            ConditionalNode cond => cond.Branches.All(b => IsNavigationOnly(b.Nodes)),
            _ => false,
        });

    // Converts a navigation-only expand-link to a navigation or conditional dict.
    private static Dictionary<string, object?> BuildNavigationFromExpand(ExpandLinkNode expand, SerializationContext? ctx) =>
        BuildNavDictFromNodes(expand.ExpandNodes, expand.Label, expand.StateAffecting, ctx);

    private static Dictionary<string, object?> BuildNavDictFromNodes(
        List<MwsNode> nodes, string label, bool stateAffecting, SerializationContext? ctx)
    {
        if (nodes is [GotoNode singleGoto])
        {
            var d = new Dictionary<string, object?>
            {
                ["type"] = "link",
                ["label"] = label,
                ["target"] = singleGoto.Target,
                ["snapshot"] = stateAffecting,
            };
            AddLinkHint(d, singleGoto.Target, ctx);
            return d;
        }
        if (nodes is [ConditionalNode cond])
        {
            var ifBranches = cond.Branches.Where(b => b.Else != true).ToList();
            var elseBranch = cond.Branches.FirstOrDefault(b => b.Else == true);

            // Flat format: single if-branch with no else
            if (ifBranches.Count == 1 && elseBranch is null)
            {
                return new Dictionary<string, object?>
                {
                    ["type"] = "conditional",
                    ["if"] = ifBranches[0].Condition,
                    ["then"] = ifBranches[0].Nodes
                        .Select(n => BuildNavDictFromNodes([n], label, stateAffecting, ctx))
                        .ToList(),
                };
            }

            // Multi-branch format
            var cd = new Dictionary<string, object?>
            {
                ["type"] = "conditional",
                ["conditions"] = ifBranches.Select(b =>
                {
                    var bd = new Dictionary<string, object?> { ["if"] = b.Condition };
                    bd["then"] = b.Nodes
                        .Select(n => BuildNavDictFromNodes([n], label, stateAffecting, ctx))
                        .ToList();
                    return bd;
                }).ToList(),
            };
            if (elseBranch is not null)
            {
                cd["else"] = elseBranch.Nodes
                    .Select(n => BuildNavDictFromNodes([n], label, stateAffecting, ctx))
                    .ToList();
            }

            return cd;
        }
        // Fallback: shouldn't be reached given IsNavigationOnly precondition
        return new Dictionary<string, object?> { ["type"] = "link", ["label"] = label, ["target"] = "", ["snapshot"] = stateAffecting };
    }

    // ── Popup ─────────────────────────────────────────────────────────────

    // Matches: ViewBiddingSystem.instance.OnShowBidding("PassageId", BiddingSystem.Voting/Bidding)
    [GeneratedRegex(@"ViewBiddingSystem\.instance\.OnShowBidding\(""([^""]+)"",\s*BiddingSystem\.(\w+)\)")]
    private static partial Regex BiddingCallPattern();

    private static bool IsSetupImageNode(MwsNode n) => n is ImageNode { Style: "setup-image" };

    private static bool StartsWithSetupImage(ConditionalBranch b) =>
        b.Nodes is [var first, ..] && IsSetupImageNode(first);

    // Splits a popup's raw child-node list into (header, content). Three shapes route (part of)
    // themselves to header, each preserving relative order:
    //   - A bare setup-image ImageNode (TryProcessSetupImageAssignment's literal case) — moves
    //     entirely to header.
    //   - A ConditionalNode whose every branch's *only* node is a setup-image ImageNode (the
    //     ternary case) — moves entirely to header; nothing is left behind for content.
    //   - A ConditionalNode where *some* branches start with a setup-image ImageNode followed by
    //     more content — by far the most common real shape, since Cradle typically sets
    //     _SetupImage as the first statement of a branch alongside branch-specific body text. Not
    //     every branch necessarily qualifies (e.g. one branch might instead be an entirely
    //     different nested popup with its own header) — split into two parallel conditionals
    //     sharing the same conditions: a header conditional (empty branch where the source branch
    //     didn't qualify) and a content conditional holding whatever's left of each branch.
    // Anything else stays in content as-is.
    private static (List<MwsNode> Header, List<MwsNode> Content) SplitPopupHeaderNodes(List<MwsNode> nodes)
    {
        List<MwsNode>? header = null;
        var content = new List<MwsNode>(nodes.Count);
        foreach (var n in nodes)
        {
            if (IsSetupImageNode(n))
            {
                (header ??= []).Add(n);
                continue;
            }

            if (n is ConditionalNode cond && cond.Branches.Count > 0 &&
                cond.Branches.Any(StartsWithSetupImage))
            {
                (header ??= []).Add(new ConditionalNode
                {
                    Branches = cond.Branches.Select(b => new ConditionalBranch
                    {
                        Condition = b.Condition,
                        Else = b.Else,
                        Nodes = StartsWithSetupImage(b) ? [b.Nodes[0]] : [],
                    }).ToList(),
                });

                var remainingBranches = cond.Branches.Select(b => new ConditionalBranch
                {
                    Condition = b.Condition,
                    Else = b.Else,
                    Nodes = StartsWithSetupImage(b) ? b.Nodes.Skip(1).ToList() : b.Nodes,
                }).ToList();
                if (remainingBranches.Any(b => b.Nodes.Count > 0))
                {
                    content.Add(new ConditionalNode { Branches = remainingBranches });
                }

                continue;
            }

            content.Add(n);
        }

        return (header ?? [], content);
    }

    private static Dictionary<string, object?> TransformPopup(ExpandLinkNode expand, SerializationContext? ctx = null)
    {
        // Scan children for layout markers:
        //   • UnknownNode with BiddingCallPattern  → layout: voting/bidding + onclose
        //   • EogSetupMarkerNode                  → layout: end_of_generation + property nodes
        //   • EndOfRoundMarkerNode                → layout: end_of_round + target/okay/onclose
        //   • SetupNotificationNode               → layout: setup + onclose (ViewItemObtain popup)
        //   • SetupBlockNode                      → unwrap body nodes directly (no section wrapper)
        string? layout = null, onclose = null;
        EogSetupMarkerNode? eogMarker = null;
        EndOfRoundMarkerNode? eorMarker = null;
        var childNodes = new List<MwsNode>(expand.ExpandNodes.Count);
        foreach (var child in expand.ExpandNodes)
        {
            if (child is UnknownNode unk)
            {
                var m = BiddingCallPattern().Match(unk.OriginalCode ?? "");
                if (m.Success)
                {
                    onclose = m.Groups[1].Value;
                    layout = m.Groups[2].Value.ToLowerInvariant();
                    continue;
                }
            }
            if (child is EogSetupMarkerNode eog)
            {
                eogMarker = eog;
                layout = "end_of_generation";
                if (eog.PassageName is not null)
                {
                    onclose = eog.PassageName;
                }

                continue;
            }
            if (child is EndOfRoundMarkerNode eor)
            {
                eorMarker = eor;
                layout = "end_of_round";
                continue;
            }
            // ViewItemObtain setup popup: the SetupNotificationNode carries the onclose passage name.
            if (child is SetupNotificationNode sn)
            {
                layout = "setup";
                if (sn.NextPassage is not null)
                {
                    onclose = sn.NextPassage;
                }

                continue;
            }
            // SetupBlockNode wraps the popup body — unwrap into childNodes directly.
            if (child is SetupBlockNode sb)
            {
                childNodes.AddRange(sb.Nodes);
                continue;
            }
            childNodes.Add(child);
        }

        // Prepend EOG property-binding nodes before any other popup content.
        if (eogMarker is not null)
        {
            var eogPropNodes = new List<MwsNode>();
            if (eogMarker.Title is not null)
            {
                eogPropNodes.Add(new LetNode { Var = "title", Compute = $"\"{MwsExprHelper.EscapeStr(eogMarker.Title)}\"" });
            }

            eogPropNodes.Add(new LetNode { Var = "completedRound", Compute = eogMarker.CompletedRound.ToString() });
            if (eogMarker.BodyText is not null)
            {
                eogPropNodes.Add(new TextNode { Template = eogMarker.BodyText });
            }

            if (eogMarker.PassageNameNodes is not null)
            {
                eogPropNodes.AddRange(eogMarker.PassageNameNodes);
            }

            childNodes.InsertRange(0, eogPropNodes);
        }

        // Prepend the end-of-round body text (ViewEndOfRound.SetEndOfRound's bodyText/bodyText2)
        // before any other popup content.
        if (eorMarker is not null)
        {
            var eorTextNodes = new List<MwsNode>();
            if (eorMarker.Body is not null)
            {
                eorTextNodes.Add(new TextNode { Template = eorMarker.Body });
            }

            if (eorMarker.Body is not null && eorMarker.Body2 is not null)
            {
                eorTextNodes.Add(new ParagraphBreakNode());
            }

            if (eorMarker.Body2 is not null)
            {
                eorTextNodes.Add(new TextNode { Template = eorMarker.Body2 });
            }

            childNodes.InsertRange(0, eorTextNodes);
        }

        var d = new Dictionary<string, object?> { ["type"] = "popup" };
        if (layout is not null)
        {
            d["layout"] = layout;
        }

        d["label"] = expand.Label;
        if (eorMarker is not null)
        {
            // Unlike the other markers' onclose, this one needs real logic (the _ProgressRound
            // assign moved out of the popup's own content by CradleExtractor.StitchFragments), not
            // just a bare navigation target — so it's built directly rather than through the
            // string-onclose path below.
            d["target"] = eorMarker.NextPassage;
            d["okay"] = "End of Round";
            d["onclose"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "assign", ["var"] = "_ProgressRound", ["expr"] = eorMarker.ProgressValue.ToString() },
            };
            AddLinkHint(d, eorMarker.NextPassage, ctx);
        }
        else if (onclose is not null)
        {
            // v0.3's bare-string `onclose` was purely a navigation target — v0.4 splits that into
            // `target` (navigation) vs. `onclose` (a node list of logic run before it). This
            // extraction path never produced onclose logic, only a destination, so it maps to `target`.
            d["target"] = onclose;
            d["okay"] = "Close";
            AddLinkHint(d, onclose, ctx);
        }
        else if (layout == "setup")
        {
            // A "setup" popup (ViewItemObtain notification) always has an acknowledgement button in
            // the original app, even when this occurrence has no destination passage (SetupNotificationNode.NextPassage
            // is null — the popup just closes in place, no engine round-trip needed since okay/target/onclose
            // are all independently optional).
            d["okay"] = "Accept";
        }
        else if (layout == "end_of_generation")
        {
            // EogSetupMarkerNode's own S_OnSetSpecialSetup path (unlike the plain S_OnEndOfGeneration
            // one in TransformEndOfGeneration) can also land here with no PassageName/target — same
            // "no footer at all" trap without an explicit okay (see TransformEndOfGeneration's note).
            d["okay"] = "Confirm";
        }

        // CheckProgress always records state, so force snapshot regardless of the source link's own
        // enchant command (None vs Replace) — matches the forcing StitchFragments used to apply when
        // this collapsed to a plain navigation LinkNode instead of a popup.
        d["snapshot"] = eorMarker is not null || expand.StateAffecting;

        var (headerNodes, contentNodes) = SplitPopupHeaderNodes(childNodes);
        var transformedHeader = TransformNodeList(headerNodes, ctx);
        if (transformedHeader.Count > 0)
        {
            d["header"] = transformedHeader;
        }

        var transformed = TransformNodeList(contentNodes, ctx);
        if (transformed.Count > 0)
        {
            d["content"] = transformed;
        }

        return d;
    }

    // ── Input action ──────────────────────────────────────────────────────

    // v0.1's InputPromptNode always came from Cradle's `OnGenerationBtn` idiom (a self-navigating
    // "show a popup, take one value, resume the same passage" pattern — see PassageBodyVisitor's
    // IsInputPromptIf/TryDetectInputPrompt). v0.4 has no standalone submit-triggered input action —
    // this maps it onto the general input-inside-popup mechanism: a guarded, auto-display popup
    // (guarded so it only shows once — testing the input's own variable for emptiness would be
    // ambiguous, e.g. 0 is a legitimate real answer for a number input, so a synthetic
    // `{var}_submitted` boolean is declared and guards it instead), containing the original prompt
    // text and the input field, with Okay setting the guard and returning to the source passage.
    private static Dictionary<string, object?> TransformInputAction(InputPromptNode input, SerializationContext? ctx = null)
    {
        var guardVar = $"{input.StoreIn}_submitted";
        ctx?.Variables?.TryAdd(guardVar, new VarDef { Name = guardVar, VarType = VarKind.Boolean });

        var popup = new Dictionary<string, object?>
        {
            ["type"] = "popup",
            ["content"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = input.Text },
                new() { ["type"] = "input", ["label"] = input.PromptId, ["var"] = input.StoreIn },
            },
            ["okay"] = "Continue",
            ["onclose"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "assign", ["var"] = guardVar, ["expr"] = "true" },
            },
            ["snapshot"] = true,
        };
        if (input.ResumePassage is not null)
        {
            popup["target"] = input.ResumePassage;
            AddLinkHint(popup, input.ResumePassage, ctx);
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "conditional",
            ["if"] = $"!{guardVar}",
            ["then"] = new List<Dictionary<string, object?>> { popup },
        };
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
        if (!string.IsNullOrEmpty(title))
        {
            d["title"] = title;
        }

        if (style is not null)
        {
            d["style"] = style;
        }

        d["content"] = TransformNodeList(innerNodes, ctx);
        return d;
    }

    // ── Conditional ───────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformConditional(ConditionalNode cond, SerializationContext? ctx = null)
    {
        var ifBranches = cond.Branches.Where(b => b.Else != true).ToList();
        var elseBranch = cond.Branches.FirstOrDefault(b => b.Else == true);

        // Flat format: single if-branch with no else
        if (ifBranches.Count == 1 && elseBranch is null)
        {
            return new Dictionary<string, object?>
            {
                ["type"] = "conditional",
                ["if"] = ifBranches[0].Condition,
                ["then"] = TransformNodeList(ifBranches[0].Nodes, ctx),
            };
        }

        // Multi-branch format
        var d = new Dictionary<string, object?>
        {
            ["type"] = "conditional",
            ["conditions"] = ifBranches.Select(b => new Dictionary<string, object?>
            {
                ["if"] = b.Condition,
                ["then"] = TransformNodeList(b.Nodes, ctx),
            }).ToList(),
        };
        if (elseBranch is not null)
        {
            d["else"] = TransformNodeList(elseBranch.Nodes, ctx);
        }

        return d;
    }

    // ── Switch ────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformSwitch(SwitchNode sw, SerializationContext? ctx = null)
    {
        var matchCases = sw.Cases.Where(c => c.Default != true).ToList();
        var defaultCase = sw.Cases.FirstOrDefault(c => c.Default == true);
        var d = new Dictionary<string, object?>
        {
            ["type"] = "switch",
            ["on"] = sw.On,
            ["cases"] = matchCases.Select(c => new Dictionary<string, object?>
            {
                ["match"] = c.Match,
                ["nodes"] = TransformNodeList(c.Nodes, ctx),
            }).ToList(),
        };
        if (defaultCase is not null)
        {
            d["default"] = TransformNodeList(defaultCase.Nodes, ctx);
        }

        return d;
    }

    // ── Foreach ───────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformForeach(ForeachNode fe, SerializationContext? ctx = null)
    {
        return new()
        {
            ["type"] = "foreach",
            ["var"] = fe.Var,
            ["in"] = fe.In,
            ["do"] = TransformNodeList(fe.Nodes, ctx),
        };
    }

    // ── Checkpoint ────────────────────────────────────────────────────────

    private static Dictionary<string, object?> TransformCheckpoint(CheckpointNode cp)
    {
        var d = new Dictionary<string, object?> { ["type"] = "checkpoint", ["id"] = cp.Id };
        if (cp.DisplayLabel is not null)
        {
            d["display"] = cp.DisplayLabel;
        }

        if (cp.DiagnosticLabel is not null)
        {
            d["diagnostic"] = cp.DiagnosticLabel;
        }

        return d;
    }

    // ── EndOfGeneration ───────────────────────────────────────────────────

    // Transforms a top-level S_OnEndOfGeneration call into a layout-driven popup.
    // The popup has no label — the end_of_generation layout drives auto-display.
    private static Dictionary<string, object?> TransformEndOfGeneration(EndOfGenerationNode eog)
    {
        var nodes = new List<Dictionary<string, object?>>();
        if (eog.Message is not null)
        {
            nodes.Add(new() { ["type"] = "text", ["value"] = eog.Message });
        }

        nodes.Add(MakeLet("generation", eog.Generation.ToString()));

        var d = new Dictionary<string, object?>
        {
            ["type"] = "popup",
            ["layout"] = "end_of_generation",
            // Every real occurrence auto-displays with no target/onclose (ViewEndOfGeneration.
            // OnConfirmBtn only navigates when PassageName was set by the separate
            // S_OnSetSpecialSetup path — see EogSetupMarkerNode/TransformPopup's own "end_of_generation"
            // layout branch for that one) — without an okay button this popup could never be dismissed
            // (RenderedPopupView renders no footer at all when both Okay and Cancel are null). The
            // reference app's Accept button reads "CONFIRM" (Main.unity GameObject 2047491225).
            ["okay"] = "Confirm",
            ["content"] = nodes,
        };
        return d;
    }

    // ── Modal ─────────────────────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformModal(ModalNode modal, SerializationContext? ctx = null)
    {
        // Transform end-of-round modal to section+checkpoint+navigation pattern
        var nodes = new List<Dictionary<string, object?>>();

        if (modal.Body is not null)
        {
            nodes.Add(new() { ["type"] = "text", ["value"] = modal.Body });
        }

        if (modal.Round.HasValue)
        {
            yield return new()
            {
                ["type"] = "section",
                ["style"] = "panel",
                ["content"] = nodes,
            };
            yield return new()
            {
                ["type"] = "checkpoint",
                ["id"] = $"round_{modal.Round}_complete",
                ["display"] = $"Round {modal.Round}",
            };
        }
        else if (nodes.Count > 0)
        {
            yield return new() { ["type"] = "section", ["style"] = "panel", ["content"] = nodes };
        }

        if (modal.Next is not null)
        {
            var label = modal.Instruction ?? "Continue";
            var navD = new Dictionary<string, object?>
            {
                ["type"] = "link",
                ["label"] = label,
                ["target"] = modal.Next,
            };
            AddLinkHint(navD, modal.Next, ctx);
            navD["snapshot"] = true;
            yield return navD;
        }
    }

    // ── SetupNotification ─────────────────────────────────────────────────

    private static IEnumerable<Dictionary<string, object?>> TransformSetupNotification(SetupNotificationNode sn, SerializationContext? ctx = null)
    {
        var nodes = new List<Dictionary<string, object?>>();
        if (sn.Title is not null)
        {
            nodes.Add(new() { ["type"] = "text", ["value"] = MwsExprHelper.WrapEmphasis(sn.Title, "**") });
        }

        if (sn.Text is not null)
        {
            nodes.Add(new() { ["type"] = "text", ["value"] = sn.Text });
        }

        yield return new() { ["type"] = "section", ["style"] = "panel", ["content"] = nodes };

        if (sn.NextPassage is not null)
        {
            var navD = new Dictionary<string, object?>
            {
                ["type"] = "link",
                ["label"] = "Continue",
                ["target"] = sn.NextPassage,
            };
            AddLinkHint(navD, sn.NextPassage, ctx);
            navD["snapshot"] = true;
            yield return navD;
        }
    }

    // Merges a standalone SetupPassagename assignment with the styleScope("setupStyleEvnt", ...)
    // block immediately following it (see the call site comment in TransformNodeList) into one
    // auto-display popup (no label). "Accept" is this popup style's Okay label in the original
    // app — distinct from the ViewItemObtain-pickup-notification's own "Close" label, which is
    // handled separately by TransformPopup for the inside-a-link-fragment case.
    private static Dictionary<string, object?> TransformSetupNotificationBlock(
        SetupNotificationNode sn, SetupBlockNode setupBlock, SerializationContext? ctx = null)
    {
        var content = new List<Dictionary<string, object?>>();
        if (sn.Title is not null)
        {
            content.Add(new() { ["type"] = "text", ["value"] = MwsExprHelper.WrapEmphasis(sn.Title, "**") });
        }

        if (sn.Text is not null)
        {
            content.Add(new() { ["type"] = "text", ["value"] = sn.Text });
        }

        var (headerNodes, contentNodes) = SplitPopupHeaderNodes(setupBlock.Nodes);
        content.AddRange(TransformNodeList(contentNodes, ctx));

        var d = new Dictionary<string, object?> { ["type"] = "popup", ["layout"] = "setup" };
        var transformedHeader = TransformNodeList(headerNodes, ctx);
        if (transformedHeader.Count > 0)
        {
            d["header"] = transformedHeader;
        }

        if (content.Count > 0)
        {
            d["content"] = content;
        }

        // Okay is always present for this layout — Cradle's own "setup" popup always has an
        // acknowledgement button, even when there's no explicit destination passage (it just falls
        // through to whatever renders next, since target/onclose are independently optional).
        d["okay"] = "Accept";
        if (sn.NextPassage is not null)
        {
            d["target"] = sn.NextPassage;
            AddLinkHint(d, sn.NextPassage, ctx);
            d["snapshot"] = true;
        }

        return d;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static Dictionary<string, object?> MakeLet(string varName, string expr,
        List<string>? lets = null)
    {
        var d = new Dictionary<string, object?> { ["type"] = "let", ["var"] = varName, ["expr"] = expr };
        if (lets is { Count: > 0 })
        {
            d["lets"] = lets;
        }

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

}
