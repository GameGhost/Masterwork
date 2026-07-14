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
    // Nodes that produce no visible output — breaks adjacent to only these are removable.
    private static bool IsNonRendered(MwsNode node) => node is
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
                setup.Nodes = Apply(setup.Nodes, mode, hasPrecedingRendered, hasFollowingRendered);
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
        var i = 0;
        while (i < nodes.Count)
        {
            var node = nodes[i];
            if (!IsBreak(node))
            {
                var followingForContainer = hasFollowingRendered || HasRenderedLater(nodes, i + 1);
                RecurseContainers(node, mode, sawRendered, followingForContainer);
                result.Add(node);
                if (!IsNonRendered(node))
                {
                    sawRendered = true;
                }

                i++;
                continue;
            }

            // Start of a break run: gather every immediately-following break AND non-rendered node
            // (e.g. a lineBreak() straddling an invisible assign, as in Cost of Disease's
            // HospitalVisitCheck2 — `lineBreak(); Vars.hospentry = ...; lineBreak();` — is still one
            // gap, not two separately-decided ones) up to the next rendered node or list end.
            var runStart = i;
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

            var isLeading = runStart == 0 && !hasPrecedingRendered;
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
                continue;
            }

            if (!isLeading && !isTrailing && breakCount == 1 && interstitials.Count == 0)
            {
                // An ordinary single break directly between two rendered nodes — never was "extra".
                result.Add(new BreakNode { SourceLine = firstBreakLine, WithinStyleScope = withinStyleScope });
                sawRendered = true;
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
