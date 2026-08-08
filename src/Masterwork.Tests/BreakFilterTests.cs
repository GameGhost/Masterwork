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
    public void LeadingBreak_AfterLeadingAssigns_IsDropped()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00027-Barventures.mws.yaml — a
        // leftover break from between the passage's heading (hoisted out to `title` upstream) and
        // its first real line, with two `assign`s (barin, gen3pg) sitting between the break and the
        // start of the list. "Leading" was computed from the break run's own list index (2, not 0)
        // instead of whether anything had actually rendered yet, so it slipped through as an
        // ordinary interior break instead of being dropped.
        var nodes = new List<MwsNode> { Assign("barin"), Assign("gen3pg"), new BreakNode(), Text("a") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["effect", "effect", "text"], result.Select(n => n.Type));
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
    public void SingleBreakTouchingLetThatFeedsNextText_IsPreserved()
    {
        // Regression: A Time of War's ResistSides — "Choose Sides" (bold heading) + lineBreak() +
        // "In turn order, each player who built at least {either-choice} may choose...". The
        // either() call inside the second sentence gets hoisted by ConsolidateTextNodes into a
        // LetNode sitting right after the break, before the sentence's own merged TextNode — a pure
        // extraction artifact (the source has no separate statement there at all, just one
        // continuous text() argument). Unlike SingleBreakTouchingAssign_IsDropped's real, unrelated
        // side-effect statement, this LetNode's value is consumed by the very next TextNode
        // (TextNode.Lets) — so the break is genuinely between two rendered sentences, not
        // decoration next to bookkeeping, and must survive.
        // Matches BreakFilter's own established interstitial-then-break ordering (e.g.
        // TwoBreaksStraddlingAnAssign_CollapseToOneParagraphBreak below) — harmless either way,
        // since a LetNode's execution never depends on its position relative to a purely visual
        // break, only on coming before the text that reads it.
        var fed = new TextNode { Template = "b {_rnd_0}", Lets = ["_rnd_0"] };
        var nodes = new List<MwsNode> { Text("a"), new BreakNode(), Let("_rnd_0"), fed };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "let", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void SingleBreakTouchingLetThatDoesNotFeedNextText_IsDropped()
    {
        // Contrast with the previous test: a LetNode whose value the following text does NOT
        // reference is exactly the same "decorative bookkeeping" shape as an Assign — must still be
        // dropped, same as SingleBreakTouchingAssign_IsDropped.
        var unrelated = new TextNode { Template = "b", Lets = ["other_var"] };
        var nodes = new List<MwsNode> { Text("a"), new BreakNode(), Let("_rnd_0"), unrelated };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "let", "text"], result.Select(n => n.Type));
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

    [Fact]
    public void LeadingAutoDisplayPopup_DoesNotCountAsRenderedForFollowingBreak()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00009-S5Fate2.mws.yaml —
        // EndOfGenerationNode always becomes an auto-display popup with no label (a separate
        // overlay, never a position in the passage's own inline flow), so a break right after it,
        // with nothing else rendered yet in the passage body itself, is still a leading break and
        // must be dropped — not preserved just because *something* (the popup) came before it.
        var nodes = new List<MwsNode> { new EndOfGenerationNode(), new BreakNode(), Text("a") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["end_of_generation", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void LeadingAutoDisplayInputPrompt_DoesNotCountAsRenderedForFollowingBreak()
    {
        // Same "separate overlay, no label" shape as EndOfGenerationNode above, for
        // InputPromptNode's own auto-display popup (see V2Serializer's
        // InputPrompt_EmitsGuardedAutoPopupConditional). Regression: Cost of Disease's NewMaster3A —
        // "switch(costA) { assigns } input-prompt(creationA) let(either hoist) lineBreak() text(...)"
        // — and Fear of the Unknown's Player1Stats..Player5Stats series (no switch, prompt is the
        // very first thing in the passage) — the prompt renders nothing, so the break right before
        // the actual first line of narration is leading and must be dropped.
        var nodes = new List<MwsNode> { new InputPromptNode(), new BreakNode(), Text("a") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["input_prompt", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void LeadingAutoDisplaySetupPopup_DoesNotCountAsRenderedForFollowingBreak()
    {
        // Regression: A Time of War's 2pFamineBidRes — an auto-show `setupStyle` popup
        // (SetupBlockNode, distinct from EndOfGenerationNode/InputPromptNode but the same "separate
        // overlay, no position in the passage's own inline flow" shape) sits before the passage's
        // own real content. Before SetupBlockNode joined IsNonRendered, the break right after it was
        // kept — treated as non-leading just because the popup came first — even though nothing had
        // actually rendered yet in the passage's own document flow.
        var nodes = new List<MwsNode> { new SetupBlockNode { Nodes = [Text("popup content")] }, new BreakNode(), Text("a") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["setup_block", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void LeadingBreakInsidePopup_AfterBareSetupNotificationMarker_IsDropped()
    {
        // Regression: A Time of War's SeedGUNS — a popup (ExpandLinkNode) whose ExpandNodes are
        // [bare SetupNotificationNode (Title/Text both null — a ViewItemObtain.SetupPassagename
        // marker consumed into the popup's own target, never content), SetupBlockNode [setup-image
        // ImageNode (always hoisted to the popup's own header), lineBreak(), let (hoisted either()
        // choice), text]]. Two things had to be fixed together for this: (1) the marker/setup-image
        // must be recognized as non-rendered so the switch/image don't fool BreakFilter into
        // thinking the popup's own content already rendered something before this break, and (2) the
        // popup's content must be isolated from the OUTER passage's own rendered state (here,
        // deliberately preceded by real "intro" text, mirroring the real passage's shape) — without
        // isolation, that outer text alone would already make the break look non-leading regardless
        // of the marker/image fix.
        var expand = new ExpandLinkNode
        {
            Label = "Click to continue...",
            StateAffecting = true,
            ExpandNodes =
            [
                new SetupNotificationNode(),
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new ImageNode { AssetRef = "image://setup/ScoreTrackMarker", Style = "setup-image" },
                        new BreakNode(),
                        new LetNode { Var = "_rnd_0", Random = new VarRandom { RandomType = "rand-between" } },
                        new TextNode { Template = "Any player with a token gains {_rnd_0}VP.", Lets = ["_rnd_0"] },
                    ],
                },
            ],
        };
        var result = BreakFilter.Apply([Text("intro"), expand], BreaksMode.Omit);

        var resultExpand = Assert.IsType<ExpandLinkNode>(result[1]);
        var setupBlock = Assert.IsType<SetupBlockNode>(resultExpand.ExpandNodes[1]);
        Assert.Equal(["image", "let", "text"], setupBlock.Nodes.Select(n => n.Type));
    }

    [Fact]
    public void LeadingBreakInsidePopup_AfterSwitchWithSetupNotificationBranches_IsDropped()
    {
        // Regression: A Time of War's PackingHeat1a — same shape as the bare-marker case above, but
        // the SetupNotificationNode marker sits as the trailing node of each of a SwitchNode's own
        // cases (alongside real per-round assigns) instead of directly in ExpandNodes — the switch
        // itself must also be recognized as fully non-rendered once its only non-assign content is a
        // bare marker.
        var sw = new SwitchNode
        {
            On = "round",
            Cases =
            [
                new SwitchCase { Match = 7, Nodes = [Assign("martweapons"), Assign("martial"), new SetupNotificationNode { NextPassage = "Martial1" }] },
                new SwitchCase { Default = true, Nodes = [Assign("martweapons"), Assign("martial"), new SetupNotificationNode { NextPassage = "Martial3" }] },
            ],
        };
        var expand = new ExpandLinkNode
        {
            Label = "Click to continue...",
            StateAffecting = true,
            ExpandNodes =
            [
                sw,
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new ImageNode { AssetRef = "image://setup/Creepy_Icon", Style = "setup-image" },
                        new BreakNode(),
                        new LetNode { Var = "_rnd_0", Random = new VarRandom { RandomType = "choose-one" } },
                        new TextNode { Template = "{heat} gains {_rnd_0}", Lets = ["_rnd_0"] },
                    ],
                },
            ],
        };
        var result = BreakFilter.Apply([Text("intro"), expand], BreaksMode.Omit);

        var resultExpand = Assert.IsType<ExpandLinkNode>(result[1]);
        var setupBlock = Assert.IsType<SetupBlockNode>(resultExpand.ExpandNodes[1]);
        Assert.Equal(["image", "let", "text"], setupBlock.Nodes.Select(n => n.Type));
    }

    [Fact]
    public void SetupNotificationNodeWithRealText_CountsAsRenderedForSurroundingBreaks()
    {
        // Contrast: a SetupNotificationNode carrying its own Title/Text (the standalone shape
        // TransformSetupNotification renders inline, e.g. Payment1ThanksB-style) is genuine visible
        // content, unlike the bare branch-marker shape above — must not be swept up into the same
        // "transparent for break purposes" treatment.
        var sn = new SetupNotificationNode { Title = "Setup Title" };
        var result = BreakFilter.Apply([sn, new BreakNode(), Text("a")], BreaksMode.Omit);
        Assert.Equal(["setup_notification", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void OrdinaryImage_CountsAsRenderedForSurroundingBreaks()
    {
        // Contrast: only the setup-image style is a header-only hoist target — an ordinary inline
        // image is real content and must not be treated as transparent for break purposes.
        var image = new ImageNode { AssetRef = "image://something" };
        var result = BreakFilter.Apply([image, new BreakNode(), Text("a")], BreaksMode.Omit);
        Assert.Equal(["image", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void PopupContent_GenuineInteriorBreak_IsPreservedRegardlessOfOuterContext()
    {
        // Confirms the ExpandLinkNode isolation fix only resets what the popup's own content
        // *borrows* from outside — it must not disturb an ordinary interior break that's genuinely
        // between two real, rendered nodes entirely within the popup's own content.
        var expand = new ExpandLinkNode
        {
            Label = "Click to continue...",
            ExpandNodes = [Text("popup line one"), new BreakNode(), Text("popup line two")],
        };
        var result = BreakFilter.Apply([Text("intro"), expand], BreaksMode.Omit);

        var resultExpand = Assert.IsType<ExpandLinkNode>(result[1]);
        Assert.Equal(["text", "break", "text"], resultExpand.ExpandNodes.Select(n => n.Type));
    }

    [Fact]
    public void SwitchWithOnlyNonRenderedCases_DoesNotCountAsRenderedForSurroundingBreaks()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00009-S5Fate2.mws.yaml — a
        // `switch (players) { case 2: heart = 3; ... default: heart = 3; }` where every case is a
        // bare assign can never put anything on screen, unlike an ordinary conditional/switch that
        // might contain real text in some branch — so it must be transparent for break purposes the
        // same way a bare assign is, letting a break directly after it still count as leading.
        var sw = new SwitchNode
        {
            On = "players",
            Cases =
            [
                new SwitchCase { Match = 2, Nodes = [Assign("heart")] },
                new SwitchCase { Default = true, Nodes = [Assign("heart")] },
            ],
        };
        var result = BreakFilter.Apply([sw, new BreakNode(), Text("a")], BreaksMode.Omit);
        Assert.Equal(["switch", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void SwitchWithOneTextCase_CountsAsRenderedForSurroundingBreaks()
    {
        // Contrast with the previous test: a switch with even one case that could render text must
        // NOT be treated as transparent — its own leading/trailing break decisions already account
        // for this correctly (existing behavior), this just confirms the new recursive check doesn't
        // over-apply to switches that really can produce visible output.
        var sw = new SwitchNode
        {
            On = "players",
            Cases =
            [
                new SwitchCase { Match = 2, Nodes = [Assign("heart")] },
                new SwitchCase { Default = true, Nodes = [Text("many players")] },
            ],
        };
        var result = BreakFilter.Apply([sw, new BreakNode(), Text("a")], BreaksMode.Omit);
        Assert.Equal(["switch", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void SingleBreakBetweenTextAndNonRenderedSwitch_IsPreserved()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00002-Fever1.mws.yaml — a break
        // between a text node and a following `switch (players) { case 2: let name = ...; ... }`
        // (every case just a `let`, so IsNonRendered per the S5Fate2 fix above) got swept up into
        // the same aggressive "single break touching non-rendered content -> decorative, drop it"
        // rule used for a bare assign/let sitting mid-sentence — but a switch/conditional is a whole
        // separate statement in the source, not an inline technicality, so a break the author placed
        // next to one must survive even though the switch itself renders nothing.
        var sw = new SwitchNode
        {
            On = "players",
            Cases = [new SwitchCase { Match = 2, Nodes = [Let("_rnd_name")] }],
        };
        var nodes = new List<MwsNode> { Text("Retrieve the heart tokens."), new BreakNode(), sw, Text("Give the Start Player token.") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        // Interstitials are emitted before the break they were gathered alongside (established
        // convention — see TwoBreaksStraddlingAnAssign_CollapseToOneParagraphBreak above).
        Assert.Equal(["text", "switch", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void SingleBreakBeforeNonRenderedConditional_IsPreserved()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00003-Hospital1.mws.yaml — a break
        // between a "Turn to page..." text and a following
        // `if (players > 3 && !Hospital1) { Hospital1 = 1; tracker = tracker - 1; }` (single branch,
        // no else, both nodes assigns — IsNonRendered) got absorbed as the conditional's interstitial
        // and dropped the same wrong way as the Fever1 case above, just with the non-rendering
        // container on the far side of the break instead of the near side.
        var cond = new ConditionalNode
        {
            Branches = [new ConditionalBranch { Condition = "players > 3 && !Hospital1", Nodes = [Assign("Hospital1"), Assign("tracker")] }],
        };
        var nodes = new List<MwsNode> { Text("Turn to page 4."), new BreakNode(), cond, Text("Place the Suspicion marker.") };
        var result = BreakFilter.Apply(nodes, BreaksMode.Omit);
        Assert.Equal(["text", "conditional", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void ExhaustiveConditional_BreakOnlyBranches_CollapsesToSingleBreak()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00003-Hospital1.mws.yaml — a
        // `if (seedy == "yes") { Vars._SetupImage = "..."; lineBreak(); } else { Vars._SetupImage =
        // "..."; lineBreak(); }` whose _SetupImage assignment gets hoisted out to the popup header
        // elsewhere (see SplitPopupHeaderNodes), leaving each branch with nothing but its own
        // trailing break — a conditional that fires the exact same break regardless of which branch
        // matches provides no real conditional behavior and must collapse to one plain break.
        var condA = new ConditionalBranch { Condition = "seedy == \"yes\"", Nodes = [new BreakNode()] };
        var elseB = new ConditionalBranch { Else = true, Nodes = [new BreakNode()] };
        var cond = new ConditionalNode { Branches = [condA, elseB] };

        var result = BreakFilter.Apply([Text("a"), cond, Text("b")], BreaksMode.Omit);

        Assert.Equal(["text", "break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void ExhaustiveConditional_EmptyBranches_CollapsesToNothing()
    {
        var condA = new ConditionalBranch { Condition = "x == 1", Nodes = [] };
        var elseB = new ConditionalBranch { Else = true, Nodes = [] };
        var cond = new ConditionalNode { Branches = [condA, elseB] };

        var result = BreakFilter.Apply([Text("a"), cond, Text("b")], BreaksMode.Omit);

        Assert.Equal(["text", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void NonExhaustiveConditional_BreakOnlyBranch_IsNotCollapsed()
    {
        // No `else` — the condition might not match at all, in which case nothing would have
        // rendered originally. Collapsing to an unconditional break would wrongly insert one.
        var condA = new ConditionalBranch { Condition = "x == 1", Nodes = [new BreakNode()] };
        var cond = new ConditionalNode { Branches = [condA] };

        var result = BreakFilter.Apply([Text("a"), cond, Text("b")], BreaksMode.Omit);

        var resultCond = Assert.IsType<ConditionalNode>(result[1]);
        Assert.Equal(["break"], resultCond.Branches[0].Nodes.Select(n => n.Type));
    }

    [Fact]
    public void CollapsedBreak_MergesWithAdjacentRealBreakIntoParagraphBreak()
    {
        var condA = new ConditionalBranch { Condition = "x == 1", Nodes = [new BreakNode()] };
        var elseB = new ConditionalBranch { Else = true, Nodes = [new BreakNode()] };
        var cond = new ConditionalNode { Branches = [condA, elseB] };

        var result = BreakFilter.Apply([Text("a"), cond, new BreakNode(), Text("b")], BreaksMode.Omit);

        Assert.Equal(["text", "paragraph_break", "text"], result.Select(n => n.Type));
    }

    [Fact]
    public void ExhaustiveSwitch_BreakOnlyCases_CollapsesToSingleBreak()
    {
        var caseA = new SwitchCase { Match = 1, Nodes = [new BreakNode()] };
        var defaultCase = new SwitchCase { Default = true, Nodes = [new BreakNode()] };
        var sw = new SwitchNode { On = "x", Cases = [caseA, defaultCase] };

        var result = BreakFilter.Apply([Text("a"), sw, Text("b")], BreaksMode.Omit);

        Assert.Equal(["text", "break", "text"], result.Select(n => n.Type));
    }
}
