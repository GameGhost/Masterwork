using Masterwork.Extractor;

namespace Masterwork.Tests;

// Targets BreakFilter.Apply directly — it runs in Program.cs's CLI pipeline (default mode: Omit),
// after CradleExtractor.Extract() returns, so ExtractorTests.cs's Extract() helper never exercises
// it. See CradleExtractor for TextNode/ConditionalNode/etc.
public class BreakFilterTests
{
    private static TextNode Text(string s = "x") => new() { Template = s };
    private static EffectNode Assign(string varName = "v") => new() { VarSets = new() { [varName] = "1" } };
    private static LetNode Let(string varName = "v") => new() { Var = varName, Compute = "1" };

    [Fact]
    public void SingleBreakBetweenText_IsPreserved()
    {
        var nodes = new List<MwsNode> { Text("a"), new BreakNode(), Text("b") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void LeadingBreak_IsDropped()
    {
        var nodes = new List<MwsNode> { new BreakNode(), Text("a") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text"], result.Select(n => n.Type));
    }

    [Fact]
    public void TrailingBreak_IsDropped()
    {
        var nodes = new List<MwsNode> { Text("a"), new BreakNode() };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text"], result.Select(n => n.Type));
    }

    [Fact]
    public void SingleBreakTouchingAssign_IsDropped()
    {
        // Matches the original (pre-fix) aggressive-strip behavior for a lone break with a
        // non-rendered neighbor — unchanged by the HospitalVisitCheck2 fix below, which only kicks
        // in when 2+ breaks straddle the invisible content.
        var nodes = new List<MwsNode> { Text("a"), new BreakNode(), Assign(), Text("b") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "effect", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void TwoBreaksStraddlingAnAssign_CollapseToOneParagraphBreak()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00131-HospitalVisitCheck2.mws.yaml
        // — Cradle's `lineBreak(); Vars.hospentry = ...; lineBreak();` used to have BOTH breaks
        // independently stripped (each had one non-rendered neighbor), leaving no separator at all
        // between the preceding text and the following conditional's own text. The pair represents
        // one paragraph gap that happens to have an invisible assign folded into it — mirrors
        // ConsolidateBreaks' "2+ consecutive breaks -> one paragraph break" rule, just tolerant of
        // the assign sitting in between.
        var nodes = new List<MwsNode> { Text("a"), new BreakNode(), Assign("hospentry"), new BreakNode(), Text("b") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "effect", "paragraph_break", "text"], result.Select(n => n.Type));
        var assign = Assert.IsType<EffectNode>(result[1]);
        Assert.Equal("hospentry", assign.VarSets!.Keys.Single());
    }

    [Fact]
    public void TwoConsecutiveBreaksNoInterstitial_CollapseToOneParagraphBreak()
    {
        var nodes = new List<MwsNode> { Text("a"), new BreakNode(), new BreakNode(), Text("b") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "paragraph_break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void AlreadyParagraphBreak_FollowedByMultipleLets_IsPreserved()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00095-InfinityClick2.mws.yaml —
        // two consecutive lineBreak()s (already merged into one ParagraphBreakNode by
        // ConsolidateBreaks, upstream of BreakFilter) followed by several hoisted `let`s for inline
        // macros1.either() calls used inside the next sentence's text() arguments. The paragraph
        // break used to be treated the same as a single decorative break with a non-rendered
        // neighbor and dropped — but a break that's *already* a paragraph break carries strong
        // intent from having merged 2+ real source breaks, and must survive regardless of how much
        // invisible logic sits next to it.
        var nodes = new List<MwsNode>
        {
            Text("heading"),
            new ParagraphBreakNode(),
            Let("_rnd_1"), Let("_rnd_2"), Let("_rnd_3"),
            Text("body"),
        };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "let", "let", "let", "paragraph_break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void BreakStraddlingTwoAssigns_BothSidesNonRendered_IsDropped()
    {
        // A lone break (breakCount == 1 in its run) touching non-rendered content on either side is
        // left exactly as it was before this fix — dropping the *pair* of breaks in the reported bug
        // was wrong specifically because there were two of them; a single break with no partner
        // still follows the original, narrower "adjacent to invisible logic -> decorative" rule.
        var nodes = new List<MwsNode> { Text("a"), Assign("x"), new BreakNode(), Assign("y"), Text("b") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "effect", "effect", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void EmitMode_LeavesEverythingUntouched()
    {
        var nodes = new List<MwsNode> { new BreakNode(), Text("a"), new BreakNode(), Assign(), new BreakNode() };
        var result = BreakFilter.Apply(nodes, BreaksMode.Emit);
        Assert.Same(nodes, result);
    }

    [Fact]
    public void EmitCommentedMode_DroppedLeadingBreak_BecomesCommentedBreakNode()
    {
        var nodes = new List<MwsNode> { new BreakNode(), Text("a") };
        var result = BreakFilter.Apply(nodes, BreaksMode.EmitCommented);
        Assert.IsType<CommentedBreakNode>(result[0]);
        Assert.Equal("text", result[1].Type);
    }

    [Fact]
    public void RecursesIntoConditionalBranches()
    {
        var branchNodes = new List<MwsNode> { new BreakNode(), Text("a") };
        var cond = new ConditionalNode
        {
            Branches = [new ConditionalBranch { Condition = "x == 1", Nodes = branchNodes }],
        };
        var result = BreakFilter.Apply([cond], BreaksMode.Omit);
        var resultCond = Assert.IsType<ConditionalNode>(Assert.Single(result));
        Assert.Equal(["text"], resultCond.Branches[0].Nodes.Select(n => n.Type));
    }

    [Fact]
    public void BranchTrailingBreak_PreservedWhenAnotherSiblingConditionalFollows()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00235-NoHospitalCons.mws.yaml —
        // several bare, mutually-independent `if (Vars.hospX == ...) { ...text...; lineBreak();
        // lineBreak(); }` blocks in a row (one per doctor), each its own single-branch
        // ConditionalNode with no else. Each branch's own trailing break used to be treated as
        // "trailing" purely by looking at the branch's own node list in isolation — but when this
        // branch's condition is true at render time, whatever sibling conditional comes right after
        // it in the popup's content list renders immediately following, so the gap is real and must
        // survive as a paragraph break.
        var branchA = new List<MwsNode> { Text("Dr. A"), new BreakNode(), Text("Move forward."), new ParagraphBreakNode() };
        var branchB = new List<MwsNode> { Text("Dr. B"), new BreakNode(), Text("Move forward."), new ParagraphBreakNode() };
        var condA = new ConditionalNode { Branches = [new ConditionalBranch { Condition = "!hospA", Nodes = branchA }] };
        var condB = new ConditionalNode { Branches = [new ConditionalBranch { Condition = "!hospB", Nodes = branchB }] };

        var result = BreakFilter.Apply([condA, condB], BreaksMode.Omit);

        var resultCondA = Assert.IsType<ConditionalNode>(result[0]);
        Assert.Equal(["text", "break", "text", "paragraph_break"], resultCondA.Branches[0].Nodes.Select(n => n.Type));
    }

    [Fact]
    public void BranchTrailingBreak_DroppedWhenNothingFollowsAtOuterLevel()
    {
        // The last sibling in the chain (nothing after it at the outer level) keeps the original,
        // still-correct "genuinely trailing" behavior — this isn't a blanket "never drop" change.
        var branchB = new List<MwsNode> { Text("Dr. B"), new BreakNode(), Text("Move forward."), new ParagraphBreakNode() };
        var condA = new ConditionalNode { Branches = [new ConditionalBranch { Condition = "!hospA", Nodes = [Text("Dr. A")] }] };
        var condB = new ConditionalNode { Branches = [new ConditionalBranch { Condition = "!hospB", Nodes = branchB }] };

        var result = BreakFilter.Apply([condA, condB], BreaksMode.Omit);

        var resultCondB = Assert.IsType<ConditionalNode>(result[1]);
        Assert.Equal(["text", "break", "text"], resultCondB.Branches[0].Nodes.Select(n => n.Type));
    }

    [Fact]
    public void BranchLeadingBreak_PreservedWhenSomethingPrecedesAtOuterLevel()
    {
        var branchB = new List<MwsNode> { new BreakNode(), Text("Dr. B") };
        var condB = new ConditionalNode { Branches = [new ConditionalBranch { Condition = "!hospB", Nodes = branchB }] };

        var result = BreakFilter.Apply([Text("intro"), condB], BreaksMode.Omit);

        var resultCondB = Assert.IsType<ConditionalNode>(result[1]);
        Assert.Equal(["break", "text"], resultCondB.Branches[0].Nodes.Select(n => n.Type));
    }
}
