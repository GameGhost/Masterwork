using System.Text.RegularExpressions;
using Masterwork.Extractor.Visitors;
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
    // Sentinel target for a link whose onclick is guaranteed to navigate in every REAL play-through
    // but isn't statically provable exhaustive (see IsLogicOnly's own remarks) — deliberately
    // distinctive so it's obvious in logs/state if it's ever actually reached, unlike a blank/empty
    // target which looks like a bug in the extractor rather than a (theoretical, never-in-practice)
    // gap in the source's own conditional logic.
    private const string UnreachableTarget = "__UNREACHABLE__";

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
                // style: "hub-card" is what my-fathers-work-template's own CSS (.mws-section.style-hub-card)
                // targets for a hub passage's individually-framed location cards — TransformSection's
                // style parameter existed for exactly this but was never actually passed at either
                // call site, so every hub-card section silently fell back to style-default (unstyled).
                result.Add(TransformSection(heading.Text, bodyNodes, style: "hub-card", headingSourceLine: heading.SourceLine, ctx: ctx));
                continue;
            }
            if (node is SectionBodyNode orphanBody)
            {
                AddSrcSentinel(result, orphanBody.SourceLine, ctx);
                result.Add(TransformSection(null, orphanBody.Nodes, style: "hub-card", headingSourceLine: orphanBody.SourceLine, ctx: ctx));
                continue;
            }

            // Pair a standalone ViewItemObtain.SetupPassagename assignment (SetupNotificationNode)
            // with the styleScope("setupStyleEvnt", ...) block immediately following it at the top
            // level of a passage. Cradle sets SetupPassagename right before that block to configure
            // where the block's own popup navigates to on Accept — it isn't a standalone
            // notification. (When the same pair instead appears inside a link(...)'s callback
            // fragment, FragmentStitchPass folds them into an ExpandLinkNode and TransformPopup
            // already merges them correctly; this handles the top-level case that bypasses that path.)
            //
            // Real Cradle source doesn't always put the SetupBlockNode immediately after the
            // SetupPassagename assignment with nothing but breaks in between — several more
            // interstitial shapes showed up here, and skipping past ONLY breaks silently broke the
            // pairing, falling back to TransformSetupNotification's standalone (title/text-less,
            // since this idiom never sets sn.Title/sn.Text) rendering: an empty `section: panel`
            // plus a bare "Continue" link, with the popup's own header/content scattered or lost
            // outright. Real occurrences: Cost of Disease's HelpingEvil2 (`Vars._SetupImage = "..."`
            // sits BEFORE entering the styleScope, not inside it — the usual case
            // SplitPopupHeaderNodes already handles is an image INSIDE setupBlock.Nodes, not one
            // preceding the block entirely), WolvesSetupGen3 (`Vars.gen3pg = 0`, an ordinary
            // session-var assign with no visual output of its own, sitting between the two), and
            // Fear of the Unknown's Witchwolf1Hex/Witchwolf2Hex (a whole if/elseif chain — 6
            // branches, each just a plain assign choosing which of witA-E/witAI feeds `witch1` —
            // sitting between the two). All tolerated now: an interstitial ImageNode is folded into
            // the popup's own header (prepended, so it still displays before whatever image the
            // block's own content contributes); an interstitial EffectNode/LetNode (a plain assign)
            // is preserved as a normal node emitted just before the popup; a ConditionalNode/
            // SwitchNode is ALSO preserved whole, but only when its ENTIRE branch/case tree produces
            // no visible output of its own (IsPreservableSetupPreamble, recursive) — a conditional
            // that itself contains real text/image content is a genuinely different, more ambiguous
            // shape than "some bookkeeping happens first," so that's still left unrecognized rather
            // than guessed at. Anything else still isn't tolerated either — bails out to the same
            // conservative standalone fallback as before.
            if (node is SetupNotificationNode standaloneSn)
            {
                var j = i + 1;
                List<MwsNode> preNodes = [];
                List<MwsNode> headerImages = [];
                var recognized = true;
                while (j < nodes.Count && nodes[j] is not SetupBlockNode)
                {
                    switch (nodes[j])
                    {
                        case BreakNode or ParagraphBreakNode:
                            break;
                        case ImageNode img:
                            headerImages.Add(img);
                            break;
                        case EffectNode or LetNode:
                            preNodes.Add(nodes[j]);
                            break;
                        case ConditionalNode or SwitchNode when IsPreservableSetupPreamble(nodes[j]):
                            preNodes.Add(nodes[j]);
                            break;
                        default:
                            recognized = false;
                            break;
                    }

                    if (!recognized)
                    {
                        break;
                    }

                    j++;
                }

                if (recognized && j < nodes.Count && nodes[j] is SetupBlockNode pairedBlock)
                {
                    AddSrcSentinel(result, standaloneSn.SourceLine, ctx);
                    foreach (var pre in preNodes)
                    {
                        var preDicts = TransformNode(pre, ctx).ToList();
                        if (preDicts.Count > 0)
                        {
                            AddSrcSentinel(result, pre.SourceLine, ctx);
                        }

                        result.AddRange(preDicts);
                    }

                    result.Add(TransformSetupNotificationBlock(standaloneSn, pairedBlock, headerImages, ctx));
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

    // True when `node` can never produce visible output of its own — used by TransformNodeList's
    // SetupNotificationNode-to-SetupBlockNode pairing to decide whether a ConditionalNode/SwitchNode
    // sitting between the two is safe to preserve whole as ordinary bookkeeping (see its own
    // remarks). Deliberately narrow, mirroring CradleExtractor's own IsHeadingInert: a branch/case
    // containing real text/image content makes the whole node NOT preservable this way, since
    // splitting visible content out from a conditional the caller only meant to pass through whole
    // would need real header/content routing decisions this pairing isn't set up to make.
    private static bool IsPreservableSetupPreamble(MwsNode node) => node switch
    {
        EffectNode or LetNode or BreakNode or ParagraphBreakNode => true,
        ConditionalNode cond => cond.Branches.All(b => b.Nodes.All(IsPreservableSetupPreamble)),
        SwitchNode sw => sw.Cases.All(c => c.Nodes.All(IsPreservableSetupPreamble)),
        _ => false,
    };

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
    // For an expression-valued target (starting with "${"), every quoted string literal
    // appearing anywhere in the expression (e.g. both branches of a ternary) that resolves to a
    // real passage gets its own hint, joined into one comment — e.g. '${peeps == 1 ? "NoUni3b" :
    // "Scoring"}' hints both "./00040-NoUni3b.mws.yaml, ./00284-Scoring.mws.yaml". A bare variable
    // reference with no literal substrings (e.g. '${Liberal2nextpsg}') yields no hint — its
    // possible values aren't statically known here (see ExtractQuotedLiterals's own remarks on
    // why this doesn't attempt to trace the variable back to its assignment sites).
    private static void AddLinkHint(Dictionary<string, object?> d, string target, SerializationContext? ctx)
    {
        if (ctx?.PassageFileMap is null)
        {
            return;
        }

        if (!target.StartsWith("${", StringComparison.Ordinal))
        {
            if (ctx.PassageFileMap.TryGetValue(target, out var file) && file is not null)
            {
                d["_link"] = file;
            }

            return;
        }

        List<string>? files = null;
        foreach (var literal in ExtractQuotedLiterals(target))
        {
            if (ctx.PassageFileMap.TryGetValue(literal, out var file) && file is not null)
            {
                files ??= [];
                if (!files.Contains(file))
                {
                    files.Add(file);
                }
            }
        }

        if (files is { Count: > 0 })
        {
            d["_link"] = string.Join(", ", files);
        }
    }

    // Extracts every double-quoted string literal substring from a raw MWS expression (e.g. a
    // ternary's branches). Deliberately shallow — a pure text scan, not real expression parsing —
    // since AddLinkHint only needs the literal branch values, not the whole expression's shape.
    // Doesn't attempt to resolve a bare variable target back to its assignment sites (e.g. finding
    // that Liberal2nextpsg is set from a shuffled ["LiberalEvent", "LiberalTaxes"] array elsewhere
    // in the same onclose/onclick list) — that's real dataflow tracing, not a text scan, and risks
    // producing misleading hints for variables whose value isn't actually constrained to a small
    // literal set. Left unhandled per the format docs' own guidance that expression targets are
    // simply not statically known.
    private static IEnumerable<string> ExtractQuotedLiterals(string exprText)
    {
        foreach (Match m in QuotedLiteralRegex().Matches(exprText))
        {
            yield return m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }

    [GeneratedRegex("\"((?:[^\"\\\\]|\\\\.)*)\"")]
    private static partial Regex QuotedLiteralRegex();

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
                    : ContainsPopupOnlyMarker(expand.ExpandNodes)
                        ? TransformPopup(expand, ctx)
                        : AlwaysNavigatesToGoto(expand.ExpandNodes)
                            ? BuildLinkWithOnClickFromExpand(expand, ctx)
                            : IsLogicOnly(expand.ExpandNodes)
                                ? BuildLinkWithOnClickFromExpand(expand, ctx, ContainsGoto(expand.ExpandNodes) ? UnreachableTarget : null)
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
        };
        if (link.Target is not null)
        {
            d["target"] = link.Target;
            AddLinkHint(d, link.Target, ctx);
        }
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
            };
            AddLinkHint(d, singleGoto.Target, ctx);
            d["snapshot"] = stateAffecting;
            return d;
        }
        if (nodes is [ConditionalNode cond])
        {
            var ifBranches = cond.Branches.Where(b => b.Else != true).ToList();
            var elseBranch = cond.Branches.FirstOrDefault(b => b.Else == true);

            // Flat format: a single if-branch, with or without an else — see TransformConditional's
            // identical reasoning (`conditions:` is only for an if/elseif/.../else chain).
            if (ifBranches.Count == 1)
            {
                var flat = new Dictionary<string, object?>
                {
                    ["type"] = "conditional",
                    ["if"] = ifBranches[0].Condition,
                    ["then"] = ifBranches[0].Nodes
                        .Select(n => BuildNavDictFromNodes([n], label, stateAffecting, ctx))
                        .ToList(),
                };
                if (elseBranch is not null)
                {
                    flat["else"] = elseBranch.Nodes
                        .Select(n => BuildNavDictFromNodes([n], label, stateAffecting, ctx))
                        .ToList();
                }

                return flat;
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
        // Fallback: shouldn't be reached given IsNavigationOnly precondition. UnreachableTarget
        // rather than an empty string so this is obviously a defensive placeholder, not silent data
        // loss, if some future IsNavigationOnly-accepted shape ever reaches here unhandled.
        return new Dictionary<string, object?> { ["type"] = "link", ["label"] = label, ["target"] = UnreachableTarget, ["snapshot"] = stateAffecting };
    }

    // True when the LAST node in `nodes` is guaranteed to fire a goto by the time it finishes —
    // either a bare GotoNode, an exhaustive (has an else) ConditionalNode whose every branch also
    // always navigates, or an exhaustive (has a default case) SwitchNode whose every case also
    // always navigates. Earlier nodes are irrelevant to this guarantee — they just run as ordinary
    // onclick prelude (assigns, lets, whatever) — so unlike IsNavigationOnly this doesn't require the
    // whole list to be nav-only, only that it can never fall through into a click that navigates
    // nowhere. Used to convert an expand-link like Cost of Disease's HospitalVisitCheck fragments
    // (an assign followed by an exhaustive if/elseif/.../else chain where every branch ends in an
    // abort/goto) into a link with onclick instead of a popup with no way to close it. The SwitchNode
    // case covers the same idiom written as an exhaustive if/elseif/else-if-int-equality chain that
    // ChainedTernaryEquality's own switch-folding upstream turns into a switch before this ever runs
    // (real occurrence: Fear of the Unknown's GainFamilyPlot fragment, an assign + several guarded
    // assigns followed by a switch on `round` with cases 1/2/default, each just a goto).
    private static bool AlwaysNavigatesToGoto(List<MwsNode> nodes) =>
        nodes.Count > 0 && nodes[^1] switch
        {
            GotoNode => true,
            ConditionalNode { Branches: var branches } when branches.Any(b => b.Else == true) =>
                branches.All(b => AlwaysNavigatesToGoto(b.Nodes)),
            SwitchNode { Cases: var cases } when cases.Any(c => c.Default == true) =>
                cases.All(c => AlwaysNavigatesToGoto(c.Nodes)),
            _ => false,
        };

    // Top-level scan (matching TransformPopup's own scan depth) for node shapes that mean this
    // expand-link's content is a *specific* popup flavor (bidding/voting, end-of-generation,
    // end-of-round, setup notification/block) regardless of whether it happens to always navigate —
    // those markers carry their own layout/okay/onclose semantics that BuildLinkWithOnClickFromExpand
    // doesn't reproduce, so they must still route through TransformPopup.
    private static bool ContainsPopupOnlyMarker(List<MwsNode> nodes) => nodes.Any(n =>
        n is EogSetupMarkerNode or EndOfRoundMarkerNode or SetupNotificationNode or SetupBlockNode ||
        (n is UnknownNode unk && BiddingCallPattern().IsMatch(unk.OriginalCode ?? "")));

    // Converts an expand-link whose content isn't itself pure navigation (IsNavigationOnly) but is
    // still guaranteed to navigate via a goto by the time it finishes (AlwaysNavigatesToGoto) into a
    // link node with target omitted and onclick set to the full content — e.g. Cradle's
    // `link(...) -> fragment: assign; if/elseif/.../else chain where every branch ends in abort(...)`
    // idiom (real occurrence: HospitalVisitCheck.mws.yaml). This used to become a popup with no
    // okay/close button — clicking it just displayed an empty, permanently-stuck popup instead of
    // navigating, since the goto lived inside content, not onclose. A goto inside onclick correctly
    // preempts target at render time (see GameSession.FollowLinkAsync), so target can be omitted.
    //
    // `fallbackTarget` (see UnreachableTarget) is for the sibling IsLogicOnly-but-not-provably-
    // AlwaysNavigatesToGoto case: content with no renderable output at all still shouldn't become a
    // reveal popup (there's nothing to reveal), but since exhaustiveness isn't proven here, target
    // can't be safely omitted the way the exhaustive case above can — a real (if never-in-practice)
    // code path could fall through onclick without ever setting a goto.
    private static Dictionary<string, object?> BuildLinkWithOnClickFromExpand(
        ExpandLinkNode expand, SerializationContext? ctx, string? fallbackTarget = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["type"] = "link",
            ["label"] = expand.Label,
        };
        if (fallbackTarget is not null)
        {
            d["target"] = fallbackTarget;
        }

        d["snapshot"] = expand.StateAffecting;
        d["onclick"] = TransformNodeList(expand.ExpandNodes, ctx);
        return d;
    }

    // True when every node in `nodes` is pure logic — EffectNode/LetNode/GotoNode/breaks, or a
    // ConditionalNode/SwitchNode whose every branch/case is ALSO pure logic, recursively — meaning
    // nothing here could ever produce visible content (text, image, section, input, nested
    // link/popup). Used alongside AlwaysNavigatesToGoto to decide whether a non-exhaustive
    // expand-link should still become a link+onclick instead of falling back to a `layout: 'reveal'`
    // popup: a reveal popup exists specifically to show content before the player picks an exit
    // link, so one with nothing to show is nonsensical regardless of whether every code path can be
    // statically proven to navigate. Real occurrence: Fear of the Unknown's LiberalEvent2ab's first
    // fragment — `assign; if (creature==2) goto A; else { if (lib=="taxes") goto B; if (lib==
    // "nationalist") goto C; }` — the else branch's two plain (non-else) ifs make this genuinely
    // non-exhaustive by CanNavigatesToGoto's own (correct) reasoning, but every one of Cradle's own
    // `lib` values is handled, so the "gap" only exists because the extractor can't prove it, not
    // because the story ever actually falls through it.
    private static bool IsLogicOnly(List<MwsNode> nodes) =>
        nodes.All(n => n switch
        {
            EffectNode or LetNode or GotoNode or BreakNode or ParagraphBreakNode => true,
            ConditionalNode cond => cond.Branches.All(b => IsLogicOnly(b.Nodes)),
            SwitchNode sw => sw.Cases.All(c => IsLogicOnly(c.Nodes)),
            _ => false,
        });

    // True when `nodes` contains a GotoNode anywhere (including nested inside a conditional/switch
    // branch). Distinguishes IsLogicOnly's two real shapes: content that CAN navigate but isn't
    // provably exhaustive (gets the UnreachableTarget sentinel — see its own remarks) from content
    // that never navigates AT ALL, which is a different, genuinely-intentional idiom — e.g. A Time
    // of War's ATOW-CommonCode: `link(...) -> fragment: { yield break; }`, a vestigial/abandoned
    // link (dead code left over from a removed feature, per the commented-out lines right above its
    // call site in the source) that's meant to do nothing when clicked, forever. Applying the
    // sentinel there would be actively wrong — clicking it would then try to navigate to a
    // passage_id that doesn't exist and throw, which is strictly worse than the reveal-popup
    // fallback it replaced. Omitting `target` entirely (this method returning false) is the correct,
    // spec-documented "clicking this link does nothing" behavior for that case instead.
    private static bool ContainsGoto(List<MwsNode> nodes) =>
        nodes.Any(n => n switch
        {
            GotoNode => true,
            ConditionalNode cond => cond.Branches.Any(b => ContainsGoto(b.Nodes)),
            SwitchNode sw => sw.Cases.Any(c => ContainsGoto(c.Nodes)),
            _ => false,
        });

    // ── Setup-notification-in-conditional collapse ─────────────────────────

    // Resolves a SetupNotificationNode's effective next-passage string, ready to drop directly into
    // `target`/`onclose`/a ternary arm. Usually just NextPassage (a literal passage id, or an
    // already-${...}-wrapped computed expression from IsSetupPassagenameAssignment's ternary
    // fallback) — but when Random is set (SetupPassagename = macros1.either(...), which can't be
    // evaluated as a literal at extraction time since Cradle draws a fresh value every call), builds
    // a ${...}-wrapped choose_one/shuffled expression from it instead. Two earlier fixes hoisted that
    // draw into an intermediate variable (a `let`, then a session `assign`) before landing here —
    // both reverted: see SetupNotificationNode.Random's own remarks for why hoisting isn't needed at
    // all — the draw expression is a pure function of that visit's PRNG state, safe to embed inline
    // anywhere this resolved string ends up used, exactly like any other computed expression.
    private static string? ResolveSetupTarget(SetupNotificationNode sn) =>
        sn.Random is not null ? $"${{{MwsExprHelper.VarRandomToExpr(sn.Random)}}}" : sn.NextPassage;

    // True when every branch of `cond` ENDS in a SetupNotificationNode with a resolvable target
    // (ResolveSetupTarget) — tolerating arbitrary preamble before it in each branch. Mirrors
    // CradleExtractor.TryCollapseCheckProgressConditional's arm-extraction, but for
    // ViewItemObtain.SetupPassagename assigns instead of CheckProgress calls, and without that
    // method's "must already have an else" requirement — see CollapseSetupNotificationConditionals
    // for why this one has to tolerate an absent else.
    //
    // A branch's preamble (real per-branch side effects — assign, etc.) is NEVER hoisted or moved:
    // only the trailing SetupNotificationNode is stripped from each branch's own Nodes list (its
    // info now lives in the merged popup-level target/layout instead) — everything else stays
    // exactly where it was, still gated by that branch's own condition, still part of the popup's
    // own content. This sidesteps the earlier BlameResolve/ParadoxFirst regression (a prior attempt
    // unconditionally HOISTED a branch's preamble out of its own gate, corrupting content when that
    // preamble was a real per-branch side effect, not just a random draw — see git history) by
    // never moving anything at all. Mutation only happens once every branch has matched — a partial
    // match must never strip anything, so a failed later branch can't leave earlier ones corrupted.
    // Real-world occurrence: A Time of War's PackingHeat1a (source line 14579) — "if (round == 7) {
    // martweapons = 1; martial = 1; SetupPassagename = "Martial1"; } else if (round == 8) { ... }
    // else { ... }", consolidated into a SwitchNode — see the SwitchNode overload below; this
    // ConditionalNode overload still matters for a chain SwitchNode-consolidation doesn't recognize
    // (e.g. compound OR conditions, or fewer than 3 branches — see TryConvertCompoundConditionalToSwitch).
    private static bool TryGetSetupTargetArms(ConditionalNode cond, out List<(string? Condition, string Target)> arms)
    {
        arms = [];
        var strippedPerBranch = new List<List<MwsNode>>();
        foreach (var branch in cond.Branches)
        {
            if (branch.Nodes is not [.. var pre, SetupNotificationNode sn] || ResolveSetupTarget(sn) is not { } target)
            {
                arms = [];
                return false;
            }

            strippedPerBranch.Add(pre);
            arms.Add((branch.Else == true ? null : branch.Condition, target));
        }

        if (arms.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < cond.Branches.Count; i++)
        {
            cond.Branches[i].Nodes = strippedPerBranch[i];
        }

        return true;
    }

    // SwitchNode counterpart of the ConditionalNode overload above — same preamble-preserving
    // semantics, but for a switch's cases/default instead of a conditional's branches.
    // BuildSwitchCaseCondition rebuilds an equivalent boolean condition per case (the inverse of
    // BuildMatchValue, used identically for the ternary-chain title-hoisting feature).
    private static bool TryGetSetupTargetArms(SwitchNode sw, out List<(string? Condition, string Target)> arms)
    {
        arms = [];
        var strippedPerCase = new List<List<MwsNode>>();
        foreach (var c in sw.Cases)
        {
            if (c.Nodes is not [.. var pre, SetupNotificationNode sn] || ResolveSetupTarget(sn) is not { } target)
            {
                arms = [];
                return false;
            }

            strippedPerCase.Add(pre);
            arms.Add((c.Default == true ? null : CradleExtractor.BuildSwitchCaseCondition(sw.On, c.Match), target));
        }

        if (arms.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < sw.Cases.Count; i++)
        {
            sw.Cases[i].Nodes = strippedPerCase[i];
        }

        return true;
    }

    // Cradle sometimes writes a ViewItemObtain popup's destination as two or more independent
    // `if` statements instead of one if/elseif/else chain — e.g. Fear of the Unknown's
    // Payment1ThanksB fragment: "if (players > 2) SetupPassagename = "Payment1C"; if (players == 2)
    // SetupPassagename = "Payment1Hub";" with no else at all (technically exhaustive since players
    // is always >= 2, but that's domain knowledge the extractor can't verify from the syntax alone).
    // TransformPopup's own top-level scan only recognizes a SetupNotificationNode as a direct
    // child, never one nested inside a ConditionalNode, so this shape fell through as ordinary
    // conditional content — never setting layout: setup or a target, instead of the setup popup the
    // reference app actually shows. This absorbs a run of consecutive sibling ConditionalNodes with
    // that shape (or a single one that already has an else) into one SetupNotificationNode whose
    // NextPassage is a computed ${...} ternary, with an empty-string terminal fallback when no arm
    // is unconditional — since the "later branches never actually get reached with an unmatched
    // value" guarantee is exactly the domain knowledge just mentioned, not something provable here.
    // Reorders `arms` so an unconditional (Condition == null) arm is last — BuildTernaryChain
    // requires its terminal arm to be the unconditional fallback — adding an empty-string fallback
    // when there isn't one (see this method's own remarks above for why that's the only safe
    // default: exhaustiveness beyond the arms we can see is domain knowledge, not provable here).
    private static void NormalizeTernaryArms(List<(string? Condition, string Target)> arms)
    {
        var elseIdx = arms.FindIndex(a => a.Condition is null);
        if (elseIdx >= 0 && elseIdx != arms.Count - 1)
        {
            var elseArm = arms[elseIdx];
            arms.RemoveAt(elseIdx);
            arms.Add(elseArm);
        }
        else if (elseIdx < 0)
        {
            arms.Add((null, ""));
        }
    }

    private static List<MwsNode> CollapseSetupNotificationConditionals(List<MwsNode> nodes)
    {
        var result = new List<MwsNode>(nodes.Count);
        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i] is ConditionalNode firstCond && TryGetSetupTargetArms(firstCond, out var arms))
            {
                var sourceLine = firstCond.SourceLine;
                var participants = new List<ConditionalNode> { firstCond };
                int j = i + 1;
                while (!arms.Any(a => a.Condition is null) &&
                       j < nodes.Count && nodes[j] is ConditionalNode nextCond &&
                       TryGetSetupTargetArms(nextCond, out var moreArms))
                {
                    arms.AddRange(moreArms);
                    participants.Add(nextCond);
                    j++;
                }

                NormalizeTernaryArms(arms);

                // Each participant already had its own trailing SetupNotificationNode stripped in
                // place by TryGetSetupTargetArms above — a branch that still has content left over
                // (the PackingHeat1a case: per-branch `assign`s preceding the SetupPassagename) stays
                // right where it was, still gated by its own condition; only a participant left with
                // nothing in every branch (the original Payment1ThanksB/SeedGUNS shape) is dropped
                // entirely, since an empty if/else has nothing left to contribute.
                foreach (var p in participants)
                {
                    if (p.Branches.Any(b => b.Nodes.Count > 0))
                    {
                        result.Add(p);
                    }
                }

                result.Add(new SetupNotificationNode
                {
                    NextPassage = "${" + CradleExtractor.BuildTernaryChain(arms) + "}",
                    SourceLine = sourceLine,
                });
                i = j;
                continue;
            }

            if (nodes[i] is SwitchNode sw && TryGetSetupTargetArms(sw, out var swArms))
            {
                NormalizeTernaryArms(swArms);

                if (sw.Cases.Any(c => c.Nodes.Count > 0))
                {
                    result.Add(sw);
                }

                result.Add(new SetupNotificationNode
                {
                    NextPassage = "${" + CradleExtractor.BuildTernaryChain(swArms) + "}",
                    SourceLine = sw.SourceLine,
                });
                i++;
                continue;
            }

            result.Add(nodes[i]);
            i++;
        }

        return result;
    }

    // ── Popup ─────────────────────────────────────────────────────────────

    // Matches: ViewBiddingSystem.instance.OnShowBidding("PassageId", BiddingSystem.Voting/Bidding)
    [GeneratedRegex(@"ViewBiddingSystem\.instance\.OnShowBidding\(""([^""]+)"",\s*BiddingSystem\.(\w+)\)")]
    private static partial Regex BiddingCallPattern();

    private static bool IsSetupImageNode(MwsNode n) => n is ImageNode { Style: "setup-image" };

    private static bool StartsWithSetupImage(ConditionalBranch b) =>
        b.Nodes is [var first, ..] && IsSetupImageNode(first);

    private static bool StartsWithSetupImage(SwitchCase c) =>
        c.Nodes is [var first, ..] && IsSetupImageNode(first);

    // Appends `node` to `content`, merging it into an immediately-preceding break instead of adding
    // a duplicate when both are break-type. BreakFilter already merges consecutive breaks, but only
    // over the tree as it existed during its own pass — a break-only conditional collapsing to a
    // single break here (see CollapseIfBreakOnly above) can newly land right next to the branch's
    // own following literal break, a pairing BreakFilter never had the chance to see.
    private static void AddContentNode(List<MwsNode> content, MwsNode node)
    {
        if (node is BreakNode or ParagraphBreakNode && content.Count > 0 && content[^1] is BreakNode or ParagraphBreakNode)
        {
            content[^1] = new ParagraphBreakNode { SourceLine = content[^1].SourceLine };
            return;
        }

        content.Add(node);
    }

    // Strips any leading/trailing break(s) from a header or content list built by
    // SplitPopupHeaderNodes. BreakFilter already trims leading/trailing breaks over the tree as it
    // existed during its own pass, but a break-only conditional collapsing to a plain break here
    // (see CollapseIfBreakOnly above) can land at the very start of `content` when the setup-image
    // it used to lead with is all that came before it (e.g. Cost of Disease's Devastation1) — a
    // position BreakFilter never got the chance to judge as leading, since this conditional didn't
    // exist as a synthesized single break until this split ran, after BreakFilter's own pass.
    private static List<MwsNode> TrimEdgeBreaks(List<MwsNode> nodes)
    {
        var start = 0;
        while (start < nodes.Count && nodes[start] is BreakNode or ParagraphBreakNode)
        {
            start++;
        }

        var end = nodes.Count;
        while (end > start && nodes[end - 1] is BreakNode or ParagraphBreakNode)
        {
            end--;
        }

        return start == 0 && end == nodes.Count ? nodes : nodes[start..end];
    }

    // Splits a popup's raw child-node list into (header, content). Shapes route (part of)
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
    //   - A SwitchNode where some cases start with a setup-image ImageNode — same shape as the
    //     ConditionalNode case above, just keyed by `match:` instead of a condition (real
    //     occurrence: Fear of the Unknown's Liberal, `switch on: creature`, where each of the
    //     `creature`-specific cases sets its own distinct setup image before diverging body text).
    //     Split into a parallel header switch (each case holding just its own leading image, or
    //     nothing if that case didn't start with one) and a content switch holding the remainder.
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

                // Stripping the setup-image node commonly leaves a branch with nothing but its own
                // trailing break (e.g. Cost of Disease's Hospital1) — a conditional that fires the
                // exact same break regardless of which branch matches isn't a real conditional
                // anymore, so collapse it the same way BreakFilter would if it had ever seen this
                // shape (it can't: this conditional is synthesized here, after BreakFilter's own
                // pass over the extractor-internal tree already ran).
                var (collapsible, replacement) = BreakFilter.CollapseIfBreakOnly(new ConditionalNode { Branches = remainingBranches });
                if (collapsible)
                {
                    if (replacement is not null)
                    {
                        AddContentNode(content, replacement);
                    }
                }
                else if (remainingBranches.Any(b => b.Nodes.Count > 0))
                {
                    content.Add(new ConditionalNode { Branches = remainingBranches });
                }

                continue;
            }

            if (n is SwitchNode sw && sw.Cases.Count > 0 && sw.Cases.Any(StartsWithSetupImage))
            {
                (header ??= []).Add(new SwitchNode
                {
                    On = sw.On,
                    Cases = sw.Cases.Select(c => new SwitchCase
                    {
                        Match = c.Match,
                        Default = c.Default,
                        Nodes = StartsWithSetupImage(c) ? [c.Nodes[0]] : [],
                    }).ToList(),
                });

                var remainingCases = sw.Cases.Select(c => new SwitchCase
                {
                    Match = c.Match,
                    Default = c.Default,
                    Nodes = StartsWithSetupImage(c) ? c.Nodes.Skip(1).ToList() : c.Nodes,
                }).ToList();

                // Same collapse-if-vacuous reasoning as the ConditionalNode case above.
                var (switchCollapsible, switchReplacement) = BreakFilter.CollapseIfBreakOnly(new SwitchNode { On = sw.On, Cases = remainingCases });
                if (switchCollapsible)
                {
                    if (switchReplacement is not null)
                    {
                        AddContentNode(content, switchReplacement);
                    }
                }
                else if (remainingCases.Any(c => c.Nodes.Count > 0))
                {
                    content.Add(new SwitchNode { On = sw.On, Cases = remainingCases });
                }

                continue;
            }

            AddContentNode(content, n);
        }

        return (TrimEdgeBreaks(header ?? []), TrimEdgeBreaks(content));
    }

    private static Dictionary<string, object?> TransformPopup(ExpandLinkNode expand, SerializationContext? ctx = null)
    {
        // Scan children for layout markers:
        //   • UnknownNode with BiddingCallPattern  → layout: voting/bidding + onclose
        //   • EogSetupMarkerNode                  → layout: end_of_generation + property nodes
        //   • EndOfRoundMarkerNode                → layout: end_of_round + target/okay/onclose
        //   • SetupNotificationNode               → layout: setup + onclose (ViewItemObtain popup)
        //   • SetupBlockNode                      → unwrap body nodes directly (no section wrapper)
        // Collapse first: the scan below only recognizes a SetupNotificationNode as a direct child,
        // not one nested inside a ConditionalNode — see CollapseSetupNotificationConditionals.
        var expandNodes = CollapseSetupNotificationConditionals(expand.ExpandNodes);
        string? layout = null, onclose = null;
        EogSetupMarkerNode? eogMarker = null;
        EndOfRoundMarkerNode? eorMarker = null;
        var childNodes = new List<MwsNode>(expand.ExpandNodes.Count);
        foreach (var child in expandNodes)
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
                else if (eog.PassageNameNodes is not null &&
                         PassageBodyVisitor.BuildTernaryExprFromLetConditionals(eog.PassageNameNodes) is { } computedTarget)
                {
                    // Computed (non-literal) passageName — e.g. A Time of War's Martial2:
                    // "string passage = warwinner == "Unified Monarchists" ? (...) : (...);
                    // S_OnSetSpecialSetup(..., passage, ...)". PassageNameNodes (the nested
                    // conditional computing it) still lands in the popup's own content below via
                    // eogPropNodes, same as before — this only additionally gives the popup a
                    // navigation target, collapsing the same tree into one ternary expression
                    // (mirrors ResolveSetupTarget's ${...} treatment for the ViewItemObtain setup
                    // popup case). Without this, the popup's `okay` button had nothing to navigate
                    // to at all.
                    onclose = "${" + computedTarget + "}";
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
                if (ResolveSetupTarget(sn) is { } resolved)
                {
                    onclose = resolved;
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

        // ViewBiddingSystem.instance.OnShowBidding(...) — the reference app's bespoke
        // countdown-then-reveal component (see docs/mws-format-latest.md §8's retired-exception
        // note). Built as a genuinely nested popup instead of a flat layout: bidding/voting popup —
        // see BuildCountdownBiddingPopup's own remarks for why, and
        // Masterwork-Modules/my-fathers-work-template's countdown_instructions/countdown_action
        // layouts for the hand-built pattern this reproduces.
        if (layout is "bidding" or "voting")
        {
            return BuildCountdownBiddingPopup(layout, onclose!, expand.Label, expand.StateAffecting, childNodes, ctx);
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
            // string-onclose path below. Any OncloseNodes (source logic that sat before the
            // terminal CheckProgress call — e.g. computing a dynamic target expression) run first,
            // ahead of the progress assign, matching source order.
            d["target"] = eorMarker.NextPassage;
            // AddLinkHint must run right after `target` is inserted (see its own doc comment) —
            // InjectSentinelComments attaches the resulting _link hint to whatever line precedes
            // it in the emitted YAML, so inserting it any later (e.g. after okay/onclose below)
            // would land the comment on the wrong line instead of the target line.
            AddLinkHint(d, eorMarker.NextPassage, ctx);
            d["okay"] = "End of Round";
            var eorOnclose = new List<Dictionary<string, object?>>();
            if (eorMarker.OncloseNodes.Count > 0)
            {
                eorOnclose.AddRange(TransformNodeList(eorMarker.OncloseNodes, ctx));
            }
            eorOnclose.Add(new() { ["type"] = "assign", ["var"] = "_ProgressRound", ["expr"] = eorMarker.ProgressValue.ToString() });
            d["onclose"] = eorOnclose;
        }
        else if (onclose is not null)
        {
            // v0.3's bare-string `onclose` was purely a navigation target — v0.4 splits that into
            // `target` (navigation) vs. `onclose` (a node list of logic run before it). This
            // extraction path never produced onclose logic, only a destination, so it maps to `target`.
            d["target"] = onclose;
            AddLinkHint(d, onclose, ctx);
            d["okay"] = "Close";
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
        else
        {
            // No known marker (setup/EOG/EOR/bidding-voting, handled above) and no plain redirect
            // target either — structurally, this popup has no way to close itself except through a
            // link inside its own content. That's exactly the "hand the device to one player, reveal
            // a private passage of text that ends in a real choice link" pattern (e.g. Cost of
            // Disease's Gen1-CreepyTrackRes) — a Harlowe enchantHook/Replace with none of the other
            // special-purpose wrappers. It's a structural rule, not a Cradle-marker match, so it also
            // covers any future occurrence of the same shape regardless of source. `layout: 'reveal'`
            // gives it a real visual treatment (my-fathers-work-template/assets/style.css) instead of
            // silently falling back to an unstyled popup; the generic Cancel lets the player back out
            // without having made a choice yet (RenderedPopupView.razor's Cancel discards the
            // sandboxed content with no commit) — styled as a corner close button, so the label text
            // itself is never actually shown.
            d["layout"] = "reveal";
            d["cancel"] = "Close";
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

    // ViewBiddingSystem's countdown-then-reveal component (Main.unity 868769489's own
    // startbuttontextlist/detailstextlist/revealText, English index 0) has no Cradle text() call of
    // its own — like ViewSpecialEvent's overlay text, this is app-side UI text, not story content,
    // so the display strings below are literal English (curated into en-US.common.restext for
    // stable keys, matching the SpecialEvent_Title precedent), not extracted from anywhere in the
    // .cs source. Built as a genuinely nested popup (a `popup` node inside another popup's own
    // `content:`) instead of the old flat `layout: bidding/voting` shape handled by the bespoke,
    // now-retired VotingPopupContent component — this matches Module-09/10A/10B exactly (ONE
    // continuous parchment note; a small dark countdown pill appears over the trigger button, the
    // instructions never disappear or change), which the old flat popup never could. See
    // docs/mws-format-latest.md §6's own worked example and
    // Masterwork-Modules/my-fathers-work-template's countdown_instructions/countdown_action layouts
    // for the hand-built pattern this reproduces (including the CSS that makes the inner popup's
    // Okay button cover the full viewport, invisibly, until the countdown animation finishes).
    private static Dictionary<string, object?> BuildCountdownBiddingPopup(
        string mode, string targetPassage, string label, bool stateAffecting, List<MwsNode> extraContent, SerializationContext? ctx)
    {
        var isVoting = mode == "voting";
        var instructions = isVoting
            ? "Click on the 'Start Voting' button to start the vote. This will start a 3-second countdown."
            : "Click on the 'Start Bidding' button to start the bid. This will start a 3-second countdown.";
        var startButton = isVoting ? "Start Voting" : "Start Bidding";

        var inner = new Dictionary<string, object?>
        {
            ["type"] = "popup",
            ["layout"] = "countdown_action",
            ["label"] = startButton,
            // Without this, the trigger renders as a plain bracketed link (style-default) instead
            // of the green button the reference screenshots show — my-fathers-work-template's own
            // hand-authored demo (Showcase_Popup_RevealCountdown.mws.yaml) already sets this; the
            // real extraction path was missing it.
            ["style"] = "countdown-trigger",
            // Four stacked text nodes (style-timed via CSS opacity keyframes, see the template's
            // own style.css), not a single node whose text changes — REVEAL needs to be
            // localizable, and a CSS `content`-cycling pseudo-element never could be.
            ["content"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = "3", ["style"] = "countdown-3" },
                new() { ["type"] = "text", ["value"] = "2", ["style"] = "countdown-2" },
                new() { ["type"] = "text", ["value"] = "1", ["style"] = "countdown-1" },
                new() { ["type"] = "text", ["value"] = "REVEAL", ["style"] = "countdown-reveal" },
            },
            ["okay"] = "REVEAL",
        };
        inner["target"] = targetPassage;
        // AddLinkHint must run right after `target` is inserted (see its own doc comment).
        AddLinkHint(inner, targetPassage, ctx);
        inner["snapshot"] = stateAffecting;

        var outerContent = new List<Dictionary<string, object?>>
        {
            new() { ["type"] = "text", ["value"] = instructions },
            new() { ["type"] = "break", ["style"] = "paragraph" },
            // Static across both modes in the reference app itself (Main.unity 819202544's own
            // fixed m_text) — still says "your bid" even in the voting flavor, not a bug introduced
            // here.
            new() { ["type"] = "text", ["value"] = "When you see the word 'REVEAL' on the screen, simultaneously show everyone your bid." },
            new() { ["type"] = "break", ["style"] = "paragraph" },
        };

        // Real Cradle occurrences never carry anything besides the bare OnShowBidding() call, but
        // stay defensive: any other content found alongside it is appended after the synthesized
        // instructions rather than silently dropped.
        if (extraContent.Count > 0)
        {
            outerContent.AddRange(TransformNodeList(extraContent, ctx));
        }

        outerContent.Add(inner);

        return new Dictionary<string, object?>
        {
            ["type"] = "popup",
            ["layout"] = "countdown_instructions",
            ["label"] = label,
            ["content"] = outerContent,
        };
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
            // 'prompt' (not 'input-popup') — matches the layout my-fathers-work-template's own
            // style.css actually styles for exactly this shape (single input + Okay, centered card,
            // footer-count-agnostic so an Okay-only popup like this one renders correctly too — see
            // its "prompt popup (single input — numeric or text)" block and its own Okay+Cancel
            // demo in 01_Entry.mws.yaml). 'input-popup' had no template CSS at all — every real
            // occurrence (Feverheart, EnvPasscode, NewMaster3A-E, ScoreEntryP1-5, ...) was rendering
            // completely unstyled.
            ["layout"] = "prompt",
            ["content"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = input.Text },
                // input.PromptId is the OnGenerationBtn call's first argument — just the popup's
                // own resume-passage value (always equal to the enclosing passage in every real
                // occurrence, see IsInputPromptIf/TryDetectInputPrompt), not a label meant for
                // display. The prompt text is already the preceding text node above; the input
                // itself gets no label.
                new() { ["type"] = "input", ["label"] = "", ["var"] = input.StoreIn },
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

        // Flat format: a single if-branch, with or without an else — `conditions:` is only needed
        // for an if/elseif/.../else chain (2+ if-branches); a plain if/else pair reads the condition
        // and both bodies directly on the node, per docs/mws-format-latest.md's own documented (and
        // already-parsed — see PassageYamlParser.BuildConditional) flat form.
        if (ifBranches.Count == 1)
        {
            var flat = new Dictionary<string, object?>
            {
                ["type"] = "conditional",
                ["if"] = ifBranches[0].Condition,
                ["then"] = TransformNodeList(ifBranches[0].Nodes, ctx),
            };
            if (elseBranch is not null)
            {
                flat["else"] = TransformNodeList(elseBranch.Nodes, ctx);
            }

            return flat;
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

    // ViewEndOfRound.instance.SetEndOfRound(...) called directly, with no enclosing clickable
    // link in source — matches the reference app's real behavior (an automatic acknowledgement
    // popup at the moment a round ends, not a narration passage the player has to click through).
    // A prior version of this transform flattened it to a plain section+checkpoint+link instead —
    // real, visible bug: no `_ProgressRound` assign anywhere, so the round-tracking variable
    // (read by the hub_early/hub_middle/hub_late layouts, same as the CheckProgress-driven
    // EndOfRoundMarkerNode path below) never advanced for this occurrence. See EndOfRoundMarkerNode
    // for the indirect (CheckProgress-driven, --progress-map-backed) path this mirrors — same
    // layout/okay/onclose shape, just built directly instead of scanning an ExpandLinkNode's
    // children, since there's no enclosing link here to scan.
    private static IEnumerable<Dictionary<string, object?>> TransformModal(ModalNode modal, SerializationContext? ctx = null)
    {
        var nodes = new List<Dictionary<string, object?>>();
        if (modal.Body is not null)
        {
            nodes.Add(new() { ["type"] = "text", ["value"] = modal.Body });
        }

        if (modal.Body2 is not null)
        {
            if (modal.Body is not null)
            {
                nodes.Add(new() { ["type"] = "break", ["style"] = "paragraph" });
            }

            nodes.Add(new() { ["type"] = "text", ["value"] = modal.Body2 });
        }

        var d = new Dictionary<string, object?>
        {
            ["type"] = "popup",
            ["layout"] = "end_of_round",
        };
        if (modal.Next is not null)
        {
            d["target"] = modal.Next;
            AddLinkHint(d, modal.Next, ctx);
        }

        d["okay"] = "End of Round";
        d["onclose"] = new List<Dictionary<string, object?>>
        {
            new() { ["type"] = "assign", ["var"] = "_ProgressRound", ["expr"] = (modal.Round ?? 0).ToString() },
        };
        d["snapshot"] = true;
        if (nodes.Count > 0)
        {
            d["content"] = nodes;
        }

        yield return d;
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

        if (ResolveSetupTarget(sn) is { } resolved)
        {
            var navD = new Dictionary<string, object?>
            {
                ["type"] = "link",
                ["label"] = "Continue",
                ["target"] = resolved,
            };
            AddLinkHint(navD, resolved, ctx);
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
        SetupNotificationNode sn, SetupBlockNode setupBlock, List<MwsNode>? extraHeaderImages = null,
        SerializationContext? ctx = null)
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
        if (extraHeaderImages is { Count: > 0 })
        {
            headerNodes = [.. extraHeaderImages, .. headerNodes];
        }

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
        if (ResolveSetupTarget(sn) is { } resolved)
        {
            d["target"] = resolved;
            AddLinkHint(d, resolved, ctx);
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
