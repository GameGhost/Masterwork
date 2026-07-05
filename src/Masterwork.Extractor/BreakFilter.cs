namespace Masterwork.Extractor;

// Node used only in emit-commented mode: serialized as a YAML comment "# - type: break"
// rather than a real node. V2Serializer emits a _commented_break sentinel for it.
internal sealed class CommentedBreakNode : MwsNode
{
    public bool IsParagraph { get; init; }
    public override string Type => IsParagraph ? "paragraph_break" : "break";
    public override Dictionary<string, object?> ToDict() =>
        throw new NotSupportedException("CommentedBreakNode is handled by V2Serializer, not ToDict()");
}

internal static class BreakFilter
{
    // Nodes that produce no visible output — breaks adjacent to only these are removable.
    private static bool IsNonRendered(MwsNode node) => node is
        EffectNode or LetNode or GotoNode or GotoMenuNode or CheckProgressNode;

    // A break is "extra" if it is trailing (no non-break node after it),
    // leading (no non-break node before it), or sandwiched between non-rendered nodes on both sides.
    private static bool IsExtraBreak(List<MwsNode> nodes, int i)
    {
        MwsNode? prev = null;
        for (int j = i - 1; j >= 0; j--)
        {
            if (nodes[j] is not BreakNode and not ParagraphBreakNode and not CommentedBreakNode)
            { prev = nodes[j]; break; }
        }

        MwsNode? next = null;
        for (int j = i + 1; j < nodes.Count; j++)
        {
            if (nodes[j] is not BreakNode and not ParagraphBreakNode and not CommentedBreakNode)
            { next = nodes[j]; break; }
        }

        if (next is null)
        {
            return true;   // trailing
        }

        if (prev is null)
        {
            return true;   // leading
        }

        return IsNonRendered(prev) || IsNonRendered(next);
    }

    // Recurse into container sub-lists and apply the filter in place.
    private static MwsNode RecurseContainers(MwsNode node, BreaksMode mode)
    {
        switch (node)
        {
            case ConditionalNode cond:
                foreach (var b in cond.Branches)
                {
                    b.Nodes = Apply(b.Nodes, mode);
                }

                break;
            case SwitchNode sw:
                foreach (var c in sw.Cases)
                {
                    c.Nodes = Apply(c.Nodes, mode);
                }

                break;
            case SectionBodyNode section:
                section.Nodes = Apply(section.Nodes, mode);
                break;
            case SetupBlockNode setup:
                setup.Nodes = Apply(setup.Nodes, mode);
                break;
            case LinkNode link when link.Nodes.Count > 0:
                link.Nodes = Apply(link.Nodes, mode);
                break;
            case ExpandLinkNode expand:
                expand.ExpandNodes = Apply(expand.ExpandNodes, mode);
                break;
            case ForeachNode fe:
                fe.Nodes = Apply(fe.Nodes, mode);
                break;
        }
        return node;
    }

    public static List<MwsNode> Apply(List<MwsNode> nodes, BreaksMode mode)
    {
        if (mode == BreaksMode.Emit)
        {
            return nodes;
        }

        var result = new List<MwsNode>(nodes.Count);
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            RecurseContainers(node, mode);

            if (node is BreakNode or ParagraphBreakNode && IsExtraBreak(nodes, i))
            {
                if (mode == BreaksMode.EmitCommented)
                {
                    result.Add(new CommentedBreakNode { IsParagraph = node is ParagraphBreakNode, SourceLine = node.SourceLine });
                }
                // else omit
            }
            else
            {
                result.Add(node);
            }
        }
        return result;
    }
}
