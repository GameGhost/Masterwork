namespace Masterwork.Extractor;

// Node used only in emit-commented mode: serialized as a YAML comment "# - type: break"
// rather than a real node. V2Serializer emits a _commented_break sentinel for it.
public sealed class CommentedBreakNode : MwsNode
{
    public bool IsParagraph { get; init; }
    public override string Type => IsParagraph ? "paragraph_break" : "break";
    public override Dictionary<string, object?> ToDict() =>
        throw new NotSupportedException("CommentedBreakNode is handled by V2Serializer, not ToDict()");
}

public static class BreakFilter
{
    // Nodes that produce no visible output in the *surrounding passage's own document flow* —
    // breaks adjacent to only these are removable. Two shapes need recursion, not just a type
    // check:
    //   - A ConditionalNode/SwitchNode renders nothing only if EVERY branch/case is itself entirely
    //     non-rendered (e.g. Cost of Disease's S5Fate2 `switch (players) { case 2: heart = 3; ... }`
    //     — every case is a bare assign, so the whole switch can never put anything on screen,
    //     unlike a conditional/switch with even one branch of real text). Exhaustiveness doesn't
    //     matter here the way it does for CollapseIfBreakOnly: whether or not a branch matches,
    //     nothing renders either way, so there's no unsafe "insert a break where none belongs" case.
    //   - EndOfGenerationNode always becomes an auto-display popup with no label (TransformEndOfGeneration
    //     never sets one) — a separate overlay, not a position in the passage's own inline flow, so a
    //     break next to it is exactly as decorative as one next to an assign.
    // InputPromptNode is the same "auto-display popup, no label" shape as EndOfGenerationNode (see
    // V2Serializer's InputPrompt_EmitsGuardedAutoPopupConditional) — real occurrence: Cost of
    // Disease's NewMaster3A, `switch(...) { assigns } if (!X_submitted) { input prompt } let
    // (either hoist) lineBreak() text(...)`, and Fear of the Unknown's whole Player1Stats..
    // Player5Stats input-collection flow (each is `if (!X_submitted) { input prompt } lineBreak()
    // text(...)`, no switch at all) — without this, the prompt counted as rendered content, so the
    // break right before the actual first line of narration wasn't recognized as leading.
    // SetupBlockNode (an auto-show `layout: setup` popup) joins EndOfGenerationNode/InputPromptNode
    // here for the same reason: it renders as a separate overlay, not a position in the surrounding
    // passage's own document flow, so a break immediately touching it is exactly as decorative as
    // one next to an assign. Real occurrence: A Time of War's 2pFamineBidRes — a leading auto-show
    // setup popup followed by a break, then the passage's own real content — the break was staying
    // (never recognized as leading) because the popup ahead of it counted as "rendered".
    private static bool IsNonRendered(MwsNode node) => node switch
    {
        EffectNode or LetNode or GotoNode or GotoMenuNode or CheckProgressNode
            or EndOfGenerationNode or InputPromptNode or SetupBlockNode => true,
        ConditionalNode cond => cond.Branches.All(b => b.Nodes.All(IsNonRendered)),
        SwitchNode sw => sw.Cases.All(c => c.Nodes.All(IsNonRendered)),
        _ => false,
    };

    // The narrower, original leaf-only set — small technical bookkeeping statements that Cradle
    // routinely leaves sitting mid-sentence (e.g. `text(); Vars.x = 1; text();`), where a single
    // break touching one of them on only one side is genuinely decorative. A non-rendering
    // ConditionalNode/SwitchNode is NOT one of these: even though it can never put anything on
    // screen (see IsNonRendered above), it's still a whole separate statement/block in the source —
    // e.g. Cost of Disease's Fever1 `switch (players) { case 2: let name = ...; ... }` sitting
    // between two real sentences, or Hospital1's `if (players > 3 && !Hospital1) { ... }` — and a
    // break the author placed next to one of those was deliberate paragraph separation, not
    // decoration around an inline technicality. Used only to decide whether a *single* break run
    // (breakCount == 1) gets the aggressive "drop it" treatment; every other decision in Apply below
    // (leading/trailing sawRendered, run-gathering, multi-break merging) correctly keeps using the
    // fuller IsNonRendered instead, since a non-rendering container must still count as transparent
    // for those — e.g. S5Fate2's leading popup+switch+lets, which really do need to be skipped over
    // to find the true first rendered content.
    private static bool IsTrivialNonRendered(MwsNode node) => node is
        EffectNode or LetNode or GotoNode or GotoMenuNode or CheckProgressNode;

    private static bool IsBreak(MwsNode node) => node is BreakNode or ParagraphBreakNode;

    // True if any node from fromIndex onward in `nodes` might produce visible output — used to tell
    // a container (e.g. one branch of a ConditionalNode) whether something renders after it once
    // control returns to the enclosing list, so a break at the very end of that branch isn't
    // mistaken for a trailing (nothing-after) break just because the branch's own list ends there.
    private static bool HasRenderedLater(List<MwsNode> nodes, int fromIndex)
    {
        for (var j = fromIndex; j < nodes.Count; j++)
        {
            if (!IsBreak(nodes[j]) && !IsNonRendered(nodes[j]))
            {
                return true;
            }
        }
        return false;
    }

    // After a ConditionalNode/SwitchNode's branches have already been recursively processed, checks
    // whether EVERY branch's surviving content is nothing but breaks (possibly none at all) — left
    // behind, for example, when a branch's only real content (e.g. a `Vars._SetupImage` assignment,
    // see Cost of Disease's Hospital1) gets hoisted out elsewhere by an earlier pass, leaving just
    // the branch's own trailing lineBreak() with no distinguishing content anymore. When every
    // branch collapses to the same nothing-but-breaks shape AND the branches are exhaustive (an
    // else/default is present — otherwise "no branch matched" would originally have rendered
    // nothing, and collapsing to an unconditional break would wrongly insert one), the whole
    // conditional is replaced by a single representative break (or removed entirely if every branch
    // was empty) rather than surviving as a no-op wrapper around it.
    // Also called directly by V2Serializer.SplitPopupHeaderNodes: the vacuous "every branch is just
    // its own trailing break" conditional that pattern leaves behind after stripping a setup-image
    // node out of each branch is synthesized at serialization time, after this whole Apply pass has
    // already run over the extractor-internal node tree — so BreakFilter itself never sees it.
    internal static (bool Collapsible, MwsNode? Replacement) CollapseIfBreakOnly(MwsNode node)
    {
        List<List<MwsNode>> branchLists;
        bool isExhaustive;
        switch (node)
        {
            case ConditionalNode cond:
                branchLists = cond.Branches.Select(b => b.Nodes).ToList();
                isExhaustive = cond.Branches.Any(b => b.Else == true);
                break;
            case SwitchNode sw:
                branchLists = sw.Cases.Select(c => c.Nodes).ToList();
                isExhaustive = sw.Cases.Any(c => c.Default == true);
                break;
            default:
                return (false, null);
        }

        if (!isExhaustive || branchLists.Count == 0 || !branchLists.All(b => b.All(IsBreak)))
        {
            return (false, null);
        }

        var allBreaks = branchLists.SelectMany(b => b).ToList();
        if (allBreaks.Count == 0)
        {
            return (true, null);
        }

        var anyParagraph = allBreaks.Any(b => b is ParagraphBreakNode) || branchLists.Any(b => b.Count >= 2);
        var withinStyleScope = allBreaks.All(b => b is BreakNode { WithinStyleScope: true } or ParagraphBreakNode { WithinStyleScope: true });
        MwsNode replacement = anyParagraph
            ? new ParagraphBreakNode { SourceLine = node.SourceLine, WithinStyleScope = withinStyleScope }
            : new BreakNode { SourceLine = node.SourceLine, WithinStyleScope = withinStyleScope };
        return (true, replacement);
    }

    // Recurse into container sub-lists and apply the filter in place. hasPrecedingRendered/
    // hasFollowingRendered describe the ENCLOSING list's own context at the point this container
    // sits — a branch's content stands in for the container in the outer flow, so its own
    // leading/trailing break decisions must account for what's actually before/after the container
    // itself, not just what's inside the branch. onclickIsolated containers (a link's onclick) are
    // a separate execution context, not sequential body content, so they reset to isolated (false,
    // false) regardless of outer context.
    private static MwsNode RecurseContainers(MwsNode node, BreaksMode mode, bool hasPrecedingRendered, bool hasFollowingRendered)
    {
        switch (node)
        {
            case ConditionalNode cond:
                foreach (var b in cond.Branches)
                {
                    b.Nodes = Apply(b.Nodes, mode, hasPrecedingRendered, hasFollowingRendered);
                }

                break;
            case SwitchNode sw:
                foreach (var c in sw.Cases)
                {
                    c.Nodes = Apply(c.Nodes, mode, hasPrecedingRendered, hasFollowingRendered);
                }

                break;
            case SectionBodyNode section:
                section.Nodes = Apply(section.Nodes, mode, hasPrecedingRendered, hasFollowingRendered);
                break;
            case SetupBlockNode setup:
                // A leading setup-image ImageNode (TryProcessSetupImageAssignment's own emission
                // order always puts it first when present) is excluded from the recursive Apply call
                // entirely, not merely reclassified as non-rendered — SplitPopupHeaderNodes
                // (V2Serializer, runs after BreakFilter) always hoists it out to the popup's own
                // `header:` section, so it never contributes to `content:`'s own leading/trailing
                // break decisions. A blanket "setup-image is non-rendered" rule in IsNonRendered
                // would be wrong here: that style also appears as an ordinary, genuinely-rendered
                // inline image directly in a passage's own top-level flow (outside any popup, where
                // no header-hoisting ever applies — real occurrence: Cost of Disease's 5Note), so the
                // distinction has to be made structurally (only within a SetupBlockNode, which is
                // exclusively popup body content) rather than by the image node's own properties
                // alone. Real occurrence needing this: A Time of War's PackingHeat1a/SeedGUNS — the
                // lineBreak() right after this image must see sawRenderedLocally still false (see
                // Apply's own remarks), which requires the image to never enter this Apply call.
                setup.Nodes = setup.Nodes is [ImageNode { Style: "setup-image" } leadingImage, .. var rest]
                    ? [leadingImage, .. Apply(rest, mode, hasPrecedingRendered, hasFollowingRendered)]
                    : Apply(setup.Nodes, mode, hasPrecedingRendered, hasFollowingRendered);
                break;
            case LinkNode link when link.Nodes.Count > 0:
                // onclick runs on click, not inline with surrounding body content — isolated.
                link.Nodes = Apply(link.Nodes, mode, hasPrecedingRendered: false, hasFollowingRendered: false);
                break;
            case ExpandLinkNode expand:
                expand.ExpandNodes = Apply(expand.ExpandNodes, mode, hasPrecedingRendered, hasFollowingRendered);
                break;
            case ForeachNode fe:
                fe.Nodes = Apply(fe.Nodes, mode, hasPrecedingRendered, hasFollowingRendered);
                break;
        }
        return node;
    }

    public static List<MwsNode> Apply(List<MwsNode> nodes, BreaksMode mode) =>
        Apply(nodes, mode, hasPrecedingRendered: false, hasFollowingRendered: false);

    private static List<MwsNode> Apply(List<MwsNode> nodes, BreaksMode mode, bool hasPrecedingRendered, bool hasFollowingRendered)
    {
        if (mode == BreaksMode.Emit)
        {
            return nodes;
        }

        var result = new List<MwsNode>(nodes.Count);
        var sawRendered = hasPrecedingRendered;
        // Unlike sawRendered, this ALWAYS starts false, ignoring hasPrecedingRendered — a purely
        // local "has anything genuinely rendered within THIS list so far" signal, used only to gate
        // the two "keep a break despite a non-trivial/feeds-next interstitial" rules below. Needed
        // because a popup's own content inherits hasPrecedingRendered=true from whatever narrative
        // text rendered before the "click to continue" link that triggers it (extremely common) —
        // real, but from OUTSIDE the popup's own content, so it must not be mistaken for something
        // having rendered INSIDE the popup already. Real occurrence: A Time of War's PackingHeat1a/
        // SeedGUNS — a popup whose own content is [switch/marker (all non-rendered), setup-image
        // (hoisted to header), lineBreak(), hoisted-either()-let, text] — inherited
        // hasPrecedingRendered made the lineBreak() look like an ordinary interior break next to the
        // let (correctly preserved elsewhere per SingleBreakTouchingLetThatFeedsNextText_IsPreserved),
        // when nothing had actually rendered yet within the popup's own content at that point.
        var sawRenderedLocally = false;
        var i = 0;
        while (i < nodes.Count)
        {
            var node = nodes[i];
            if (!IsBreak(node))
            {
                var followingForContainer = hasFollowingRendered || HasRenderedLater(nodes, i + 1);
                RecurseContainers(node, mode, sawRendered, followingForContainer);

                var (collapsible, replacement) = CollapseIfBreakOnly(node);
                if (collapsible)
                {
                    if (replacement is null)
                    {
                        // Every branch was vacuous — the conditional/switch disappears entirely.
                        i++;
                        continue;
                    }

                    // Substitute the synthesized break in place and re-enter the loop at the same
                    // index, so it flows through the ordinary break-run-gathering logic below
                    // (correctly merging with any real break immediately before/after it too).
                    nodes = [.. nodes[..i], replacement, .. nodes[(i + 1)..]];
                    continue;
                }

                result.Add(node);
                if (!IsNonRendered(node))
                {
                    sawRendered = true;
                    sawRenderedLocally = true;
                }

                i++;
                continue;
            }

            // Start of a break run: gather every immediately-following break AND non-rendered node
            // (e.g. a lineBreak() straddling an invisible assign, as in Cost of Disease's
            // HospitalVisitCheck2 — `lineBreak(); Vars.hospentry = ...; lineBreak();` — is still one
            // gap, not two separately-decided ones) up to the next rendered node or list end.
            var interstitials = new List<MwsNode>();
            var breakCount = 0;
            var anyParagraph = false;
            var withinStyleScope = true;
            var firstBreakLine = node.SourceLine;
            while (i < nodes.Count && (IsBreak(nodes[i]) || IsNonRendered(nodes[i])))
            {
                var followingForContainer = hasFollowingRendered || HasRenderedLater(nodes, i + 1);
                RecurseContainers(nodes[i], mode, sawRendered, followingForContainer);
                switch (nodes[i])
                {
                    case ParagraphBreakNode pb:
                        breakCount++;
                        anyParagraph = true;
                        withinStyleScope &= pb.WithinStyleScope;
                        break;
                    case BreakNode b:
                        breakCount++;
                        withinStyleScope &= b.WithinStyleScope;
                        break;
                    default:
                        interstitials.Add(nodes[i]);
                        break;
                }

                i++;
            }

            // "Leading" means nothing rendered has been seen yet in this list AND nothing was
            // guaranteed to render before the list even started (propagated from an enclosing
            // container) — NOT merely "this run starts at list index 0". Cost of Disease's
            // Barventures has two leading `assign`s (barin, gen3pg) before its first real break; by
            // runStart alone the break sits at index 2, so it slipped past this check entirely and
            // survived as an ordinary "between two rendered nodes" break even though nothing had
            // rendered before it. sawRendered — tracked across the whole loop, untouched by
            // interstitials — is the correct signal. (isTrailing needs no equivalent fix: the
            // run-gathering loop above only stops once it hits a genuinely rendered node or the list
            // end, so `i < nodes.Count` here always means a rendered node is next.)
            var isLeading = !sawRendered && !hasPrecedingRendered;
            var isTrailing = i >= nodes.Count && !hasFollowingRendered;

            result.AddRange(interstitials);

            if (!isLeading && !isTrailing && (breakCount >= 2 || anyParagraph))
            {
                // Interior gap with 2+ breaks (however split up by invisible logic), or a single
                // break that's already a paragraph break from an earlier consolidation pass (e.g.
                // Cost of Disease's InfinityClick2 — two lineBreak()s already merged by
                // ConsolidateBreaks, then followed by several hoisted `let`s for inline either()
                // calls) — always collapses to/survives as a single paragraph break. A break already
                // carrying paragraph intent must never be silently dropped just because it happens
                // to sit next to non-rendered logic.
                result.Add(new ParagraphBreakNode { SourceLine = firstBreakLine, WithinStyleScope = withinStyleScope });
                sawRendered = true;
                sawRenderedLocally = true;
                continue;
            }

            // A trivial LetNode interstitial (see IsTrivialNonRendered) is only "decorative
            // bookkeeping the break happens to sit next to" when it's a genuinely separate
            // statement — not when it's PassageBodyVisitor's own hoist of an inline either()/
            // random() call out of the very next TextNode's own template (TextNode.Lets records
            // exactly this: which hoisted lets a template consumes). That hoist is a pure
            // extraction artifact with no source-level existence of its own — from the author's
            // perspective the "sentence" is one continuous unit, so a break landing next to the
            // hoisted let is really a break between the PRECEDING content and that whole sentence,
            // not decorative filler next to a side-effect statement. Real-world regression: A Time
            // of War's ResistSides — "Choose Sides" (bold heading) + lineBreak() + "In turn order,
            // each player who built at least {either-choice} may choose..." — the lineBreak() was
            // being silently dropped because the hoisted random-choice let for the either() landed
            // directly after it, with no break-preserving distinction from an unrelated `Vars.x =
            // 1;` assign (which correctly stays dropped — see SingleBreakTouchingAssign_IsDropped).
            // Unlike the interstitials.Any(!IsTrivialNonRendered) rule below (a non-rendering
            // switch/conditional is a whole separate statement — always grounds to keep the break,
            // regardless of what's rendered earlier — see Fever1/Hospital1), this rule is ALSO
            // gated on sawRenderedLocally: a hoisted let by itself carries no such "separate
            // statement" weight (it's a pure extraction artifact — see the remarks above), so it's
            // only grounds to KEEP the break if something has ALSO actually rendered earlier in THIS
            // SAME list already; otherwise it's still a leading break, just one whose
            // hasPrecedingRendered happens to be inherited=true from outside a popup rather than
            // false (see sawRenderedLocally's own remarks — A Time of War's PackingHeat1a/SeedGUNS).
            var nextFeedsFromInterstitialLet = sawRenderedLocally &&
                i < nodes.Count && nodes[i] is TextNode { Lets: { } feedsLets } &&
                interstitials.OfType<LetNode>().Any(l => feedsLets.Contains(l.Var));

            if (!isLeading && !isTrailing && breakCount == 1 &&
                (interstitials.Count == 0 || interstitials.Any(n => !IsTrivialNonRendered(n)) || nextFeedsFromInterstitialLet))
            {
                // An ordinary single break directly between two rendered nodes — never was "extra" —
                // OR one touching a non-rendering conditional/switch/EndOfGenerationNode, which is a
                // whole separate statement/block, not decorative inline bookkeeping (see
                // IsTrivialNonRendered) — Cost of Disease's Fever1/Hospital1 both have real breaks
                // the author placed right next to one of these that must survive.
                result.Add(new BreakNode { SourceLine = firstBreakLine, WithinStyleScope = withinStyleScope });
                sawRendered = true;
                sawRenderedLocally = true;
                continue;
            }

            // Leading, trailing, or a single break directly touching non-rendered content on the
            // side with nothing else to separate it from — decorative, drop it (optionally leaving
            // a comment marker in EmitCommented mode).
            if (mode == BreaksMode.EmitCommented)
            {
                result.Add(new CommentedBreakNode { IsParagraph = anyParagraph, SourceLine = firstBreakLine });
            }
        }

        return result;
    }
}
