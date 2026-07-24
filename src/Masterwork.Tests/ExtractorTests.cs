using Masterwork.Extractor;

namespace Masterwork.Tests;

public class ExtractorTests
{
    private static List<MwsPassage> Extract(string source) => Extract(source, out _);

    private static List<MwsPassage> Extract(string source, out ExtractionReport report) =>
        Extract(source, ProgressMapper.Empty(), out report);

    private static List<MwsPassage> Extract(string source, ProgressMapper progressMapper, out ExtractionReport report)
    {
        var tempFile = System.IO.Path.GetTempFileName() + ".cs";
        System.IO.File.WriteAllText(tempFile, source);
        try
        {
            var opts = new ExtractionOptions { InputDir = tempFile, PassagesOutDir = "", IncludeDebug = true };
            report = new ExtractionReport();
            var extractor = new CradleExtractor(opts, SpriteMapper.Empty(), report, progressMapper);
            return extractor.Extract([tempFile]);
        }
        finally { System.IO.File.Delete(tempFile); }
    }

    private static ProgressMapper MakeProgressMapper(string json)
    {
        var tempFile = System.IO.Path.GetTempFileName() + ".json";
        System.IO.File.WriteAllText(tempFile, json);
        try
        {
            return ProgressMapper.FromJsonFile(tempFile);
        }
        finally { System.IO.File.Delete(tempFile); }
    }

    // ── Passage registration ───────────────────────────────────────────────

    [Fact]
    public void SinglePassage_IsRegistered()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["TestPassage"] = new StoryPassage("TestPassage",
                    new string[] { "ck" },
                    new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield break;
            }
            """);

        Assert.Single(passages);
        Assert.Equal("TestPassage", passages[0].PassageId);
    }

    [Fact]
    public void HubTag_SetsHubLayout()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Hub1"] = new StoryPassage("Hub1",
                    new string[] { "ck" },
                    new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield break;
            }
            """);

        Assert.Equal("hub", passages[0].Layout);
    }

    [Fact]
    public void HUBTag_AlsoSetsHubLayout()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Hub1"] = new StoryPassage("Hub1",
                    new string[] { "HUB" },
                    new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield break;
            }
            """);

        Assert.Equal("hub", passages[0].Layout);
    }

    [Fact]
    public void IntroTag_SetsIntroductionLayout()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1",
                    new string[] { "INTRO" },
                    new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield break;
            }
            """);

        Assert.Equal("introduction", passages[0].Layout);
    }

    [Fact]
    public void NoTags_StaysNarrationLayout()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1",
                    new string[] { },
                    new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield break;
            }
            """);

        Assert.Equal("narration", passages[0].Layout);
    }

    // ── Text extraction ────────────────────────────────────────────────────

    [Fact]
    public void PlainText_EmitsTextNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("Hello world");
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.Equal("Hello world", textNode.Template);
    }

    [Fact]
    public void InlineHtmlBoldTag_ConvertsToMarkdownBold()
    {
        // Regression: a literal <b>...</b> tag embedded directly in a text() string argument (as
        // opposed to Cradle's own styleScope("bold", true) idiom) used to be silently stripped by
        // SpriteMapper's HTML-tag cleanup instead of converted — losing the bold formatting
        // entirely. 27 real occurrences of inline <b>/<i> tags exist in the Cost of Disease source.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("Return all <b>Dubious Bartering</b> cards to the box.");
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.Equal("Return all **Dubious Bartering** cards to the box.", textNode.Template);
    }

    [Fact]
    public void BoldStyleScope_AppliesBoldStyle()
    {
        // A leading Vars assignment (produces an AssignNode, not a TextNode) so this bold text
        // isn't at nodes[0] and isn't intercepted by the hub/narration heading-hoist
        // (TryHoistHeadingTitleSubtitle only matches a *leading* bold TextNode) — this test is
        // about bold style application, not the heading feature; see HeadingHoist tests for that.
        // "ck2" used to be usable for this (a layout the hoist didn't apply to at all), but "ck2"
        // now prepends a synthesized special-event node instead of picking a distinct layout — see
        // Ck2Tag_PrependsSpecialEventOverlay_AndStillHoistsHeadingNormally.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.dummy = 1;
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Bold text");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.Equal("bold", textNode.Style);
    }

    [Fact]
    public void MixedStyleRuns_TrailingSpaceInBoldRun_MovesOutsideClosingDelimiter()
    {
        // Regression test: ConsolidateTextNodes' own BuildTemplate has a *separate* implementation
        // of the same runs-to-markdown logic as MwsExprHelper.BuildValueFromRuns (used when several
        // sibling text() calls merge into one TextNode) — this exercises that path specifically,
        // since real content (e.g. Fever1's "...**gain 1 **{icon:creepy_icon}.") showed it had the
        // same whitespace-flanking bug even after BuildValueFromRuns itself was fixed.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("They will ");
                using (base.styleScope("bold", true))
                {
                    yield return base.text("lose a Servant");
                }
                yield return base.text(" and ");
                using (base.styleScope("bold", true))
                {
                    yield return base.text("gain 1 ");
                }
                yield return base.text("more.");
                StyleScope styleScope = null;
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.Equal("They will **lose a Servant** and **gain 1** more.", textNode.Template);
    }

    [Fact]
    public void VarInterpolation_EmitsTemplateSyntax()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text(this.Vars.townname);
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.Equal("{townname}", textNode.Template);
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    [Fact]
    public void NavigationLink_EmitsLinkNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.link("Click here", "NextPassage", null);
                yield break;
            }
            """);

        var linkNode = passages[0].Nodes.OfType<LinkNode>().First();
        Assert.Equal("Click here", linkNode.Label);
        Assert.Equal("NextPassage", linkNode.Target);
        Assert.True(linkNode.StateAffecting);
    }

    [Fact]
    public void AbortWithLiteral_EmitsGotoNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.abort("EndPassage");
                yield break;
            }
            """);

        var gotoNode = passages[0].Nodes.OfType<GotoNode>().First();
        Assert.Equal("EndPassage", gotoNode.Target);
    }

    // ── Variable effects ───────────────────────────────────────────────────

    [Fact]
    public void IntLiteralAssignment_EmitsVarSets()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.round = 1;
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        Assert.NotNull(effect.VarSets);
        Assert.Equal(1, effect.VarSets!["round"]);
    }

    [Fact]
    public void SetupImageAssignment_StringLiteral_EmitsImageNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars._SetupImage = "StorybookToken";
                yield break;
            }
            """);

        var image = Assert.IsType<ImageNode>(passages[0].Nodes.Single());
        Assert.Equal("image://setup/StorybookToken", image.AssetRef);
        Assert.Equal("setup-image", image.Style);
    }

    [Fact]
    public void SetupImageAssignment_Ternary_EmitsConditionalNodeWithTwoImageNodes()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars._SetupImage = this.Vars.society == "Fraternity of Hunters" ? "S1_HunterToken" : "S1_WolfToken";
                yield break;
            }
            """);

        var cond = Assert.IsType<ConditionalNode>(passages[0].Nodes.Single());
        Assert.Equal(2, cond.Branches.Count);

        var thenBranch = Assert.Single(cond.Branches, b => b.Else != true);
        var thenImage = Assert.IsType<ImageNode>(Assert.Single(thenBranch.Nodes));
        Assert.Equal("image://setup/S1_HunterToken", thenImage.AssetRef);
        Assert.Equal("setup-image", thenImage.Style);

        var elseBranch = Assert.Single(cond.Branches, b => b.Else == true);
        var elseImage = Assert.IsType<ImageNode>(Assert.Single(elseBranch.Nodes));
        Assert.Equal("image://setup/S1_WolfToken", elseImage.AssetRef);
    }

    [Fact]
    public void SetupImageAssignment_UnrecognizedShape_FallsBackAndWarns()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars._SetupImage = this.Vars.someRef;
                yield break;
            }
            """, out var report);

        // Not a literal or a two-branch string ternary — falls back to ordinary assignment
        // handling (a dead EffectNode, same as before this feature existed), not an ImageNode.
        Assert.DoesNotContain(passages[0].Nodes, n => n is ImageNode or ConditionalNode);

        var tempReportPath = System.IO.Path.GetTempFileName();
        try
        {
            report.Write(tempReportPath);
            var written = System.IO.File.ReadAllText(tempReportPath);
            Assert.Contains("_SetupImage uses an unrecognized expression shape", written);
        }
        finally
        {
            System.IO.File.Delete(tempReportPath);
        }
    }

    [Fact]
    public void SetupImageAssignment_BetweenTextCalls_MergesSentenceAroundImage()
    {
        // Regression: ForScience.mws.yaml — Vars._SetupImage sitting between two text() calls that
        // form one sentence used to break the text-consolidation merge group (the image node isn't
        // a TextNode/LetNode, so ConsolidateTextNodes flushed on it), extracting "The " as its own
        // orphaned segment. The image is routed to the popup header regardless of its position in
        // the content list (SplitPopupHeaderNodes), so it never needed to interrupt the sentence.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("The ");
                this.Vars._SetupImage = "Creepy_Icon";
                yield return base.text("least player loses.");
                yield break;
            }
            """);

        var image = Assert.Single(passages[0].Nodes.OfType<ImageNode>());
        Assert.Equal("setup-image", image.Style);
        var text = Assert.Single(passages[0].Nodes.OfType<TextNode>());
        Assert.Equal("The least player loses.", text.Template);
    }

    // ── Sentence fragmented by complementary-range conditionals ─────────────

    [Fact]
    public void ComplementaryNumericRangeConditionals_SandwichedByText_MergeIntoOneConditional()
    {
        // Regression: EquitableValues.mws.yaml / UniEvent2-Failure.mws.yaml — Cradle's
        // `if (Vars.players <= 3) {...} if (Vars.players >= 4) {...}` idiom (two adjacent bare ifs,
        // not if/else) used to pick alternate wording mid-sentence extracted as 4 disjoint segments
        // (prefix text, conditional, conditional, suffix text) that read as a broken fragment. Since
        // the two conditions are a provably exhaustive, non-overlapping range split on the same
        // variable, they merge into one if/else-if ConditionalNode with the prefix/suffix text
        // folded into each branch, producing two complete sentences.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("The ");
                if (this.Vars.players <= 3)
                {
                    yield return base.text("player with the fewest VP gains 1");
                }
                if (this.Vars.players >= 4)
                {
                    yield return base.text("2 players with the fewest VP gain 1");
                }
                yield return base.text(" of their Servants from Lost.");
                yield break;
            }
            """);

        var cond = Assert.IsType<ConditionalNode>(passages[0].Nodes.Single());
        Assert.Equal(2, cond.Branches.Count);

        var branchLe3 = Assert.Single(cond.Branches, b => b.Condition == "players <= 3");
        var text1 = Assert.IsType<TextNode>(Assert.Single(branchLe3.Nodes));
        Assert.Equal("The player with the fewest VP gains 1 of their Servants from Lost.", text1.Template);

        var branchGe4 = Assert.Single(cond.Branches, b => b.Condition == "players >= 4");
        var text2 = Assert.IsType<TextNode>(Assert.Single(branchGe4.Nodes));
        Assert.Equal("The 2 players with the fewest VP gain 1 of their Servants from Lost.", text2.Template);
    }

    [Fact]
    public void NonComplementaryConditionals_DifferentVariables_NeverMerged()
    {
        // Regression guard: DevEventCure.mws.yaml / Gen1Creepy-ConcealExpose.mws.yaml use the same
        // "text, bare if, bare if, text" shape, but on two DIFFERENT variables (wolves/hunters) that
        // aren't provably mutually exclusive — both could be true (or neither), so merging into an
        // if/else-if would silently drop a clause the source always shows. Must stay two separate
        // conditionals with the surrounding text untouched.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("An envoy from the ");
                if (this.Vars.wolves == "evil")
                {
                    yield return base.text("Order of St. Hubertus");
                }
                if (this.Vars.hunters == "evil")
                {
                    yield return base.text("Fraternity of Hunters");
                }
                yield return base.text(" arrived.");
                yield break;
            }
            """);

        Assert.Equal(2, passages[0].Nodes.OfType<ConditionalNode>().Count());
        Assert.Contains(passages[0].Nodes, n => n is TextNode t && t.Template == "An envoy from the ");
        Assert.Contains(passages[0].Nodes, n => n is TextNode t && t.Template == " arrived.");
    }

    [Fact]
    public void NonComplementaryConditionals_ThreeIndependentBranches_NeverMerged()
    {
        // Regression guard: END-UniGood.mws.yaml — three independent boolean flags, each
        // additively appending its own clause (0-3 may fire), not a two-way split. Must be left
        // as three separate conditionals.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("The town ");
                if (this.Vars.cured == 1)
                {
                    yield return base.text("was cured");
                }
                if (this.Vars.uni == "yes")
                {
                    yield return base.text("built a university");
                }
                if (this.Vars.ultimate == "yes")
                {
                    yield return base.text("achieved perfection");
                }
                yield return base.text(" in the end.");
                yield break;
            }
            """);

        Assert.Equal(3, passages[0].Nodes.OfType<ConditionalNode>().Count());
    }

    [Fact]
    public void AddArithmetic_EmitsVarMath()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.tracker = int.Parse(this.Vars.tracker) + 2;
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        Assert.NotNull(effect.VarMath);
        Assert.Equal("+2", effect.VarMath!["tracker"]);
    }

    [Fact]
    public void EitherAssignment_EmitsVarRandom()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.wolves = this.macros1.either(new StoryVar[] { "evil", "good" });
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        Assert.NotNull(effect.VarRandom);
        var rand = effect.VarRandom!["wolves"];
        Assert.Equal("choose-one", rand.RandomType);
        Assert.Equal(new List<object> { "evil", "good" }, rand.Values);
    }

    // ── Conditionals ───────────────────────────────────────────────────────

    [Fact]
    public void IfElse_EmitsConditionalNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.players == 2)
                {
                    yield return base.text("two players");
                }
                else
                {
                    yield return base.text("more players");
                }
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().First();
        Assert.Equal(2, cond.Branches.Count);
        Assert.NotNull(cond.Branches[0].Condition);
        Assert.True(cond.Branches[1].Else);
    }

    // ── Section structure ──────────────────────────────────────────────────

    [Fact]
    public void HubTitleScope_EmitsSectionHeadingNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hubTitle", true))
                {
                    yield return base.text("Build a Hospital");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        var heading = passages[0].Nodes.OfType<SectionHeadingNode>().First();
        Assert.Equal("Build a Hospital", heading.Text);
    }

    [Fact]
    public void HeadingScope_IconThenSpaceRunThenLeadingSpaceBoldRun_CollapsesDoubleSpace()
    {
        // Regression test: BuildHeadingTemplate (used for "heading" styleScope, distinct from both
        // MwsExprHelper.BuildValueFromRuns and ConsolidateTextNodes' BuildTemplate) has its own copy
        // of the same runs-to-markdown logic — real content (e.g. "{icon:storybook}  **Suspicion**")
        // showed it had the same double-space gap even after the other two copies were fixed.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("heading", true))
                {
                    yield return base.text("<sprite=\"Storybook\" index=0>");
                    yield return base.text(" ");
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text(" Suspicion");
                    }
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        var heading = passages[0].Nodes.OfType<SectionHeadingNode>().First();
        Assert.Equal("{icon:storybook} **Suspicion**", heading.Text);
    }

    // ── Cradle cleanup filtering ───────────────────────────────────────────

    [Fact]
    public void StyleScopeCleanup_IsFiltered()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("text");
                StyleScope styleScope = null;
                styleScope = null;
                StyleScope styleScope2 = null;
                styleScope2 = null;
                yield break;
            }
            """);

        // Only one text node — no UnknownNode from cleanup statements
        Assert.DoesNotContain(passages[0].Nodes, n => n is UnknownNode);
    }

    // ── Text consolidation ─────────────────────────────────────────────────

    [Fact]
    public void ConsecutiveText_MergesIntoTemplate()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("For the monsters of ");
                yield return base.text(this.Vars.townname);
                yield return base.text(", these were no longer...");
                yield break;
            }
            """);

        var textNodes = passages[0].Nodes.OfType<TextNode>().ToList();
        Assert.Single(textNodes);
        Assert.Equal("For the monsters of {townname}, these were no longer...", textNodes[0].Template);
    }

    [Fact]
    public void InlineEither_EmitsLetThenTemplate()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("The ");
                yield return base.text(this.macros1.either(new StoryVar[] { "dark", "light" }));
                yield return base.text(" night");
                yield break;
            }
            """);

        var letNode = passages[0].Nodes.OfType<LetNode>().First();
        Assert.NotNull(letNode.Random);
        Assert.Equal("choose-one", letNode.Random!.RandomType);
        Assert.Equal(new List<object> { "dark", "light" }, letNode.Random.Values);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.NotNull(textNode.Template);
        Assert.Contains(letNode.Var, textNode.Template);
        Assert.Contains("The ", textNode.Template);
        Assert.Contains(" night", textNode.Template);
    }

    [Fact]
    public void UniformBoldScope_HoistsStyleToNode()
    {
        // See BoldStyleScope_AppliesBoldStyle's comment — a leading no-op assignment dodges the
        // heading-hoist instead of relying on a non-hoisting tag/layout, which no longer exists.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.dummy = 1;
                using (base.styleScope("bold", true))
                {
                    yield return base.text("All ");
                    yield return base.text(this.Vars.name);
                    yield return base.text(" bold");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.Equal("bold", textNode.Style);
        Assert.Equal("All {name} bold", textNode.Template);
    }

    // ── Heading (title/subtitle) hoisting ────────────────────────────────────

    [Fact]
    public void HubLayout_SingleBoldLineNoDash_HoistsToTitleOnly()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("The Town Hall");
                }
                StyleScope styleScope = null;
                yield return base.lineBreak();
                yield return base.text("Welcome back.");
                yield break;
            }
            """);

        Assert.Equal("The Town Hall", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.DoesNotContain(passages[0].Nodes, n => n is TextNode t && t.Template == "The Town Hall");
        var body = passages[0].Nodes.OfType<TextNode>().Single();
        Assert.Equal("Welcome back.", body.Template);
    }

    [Fact]
    public void HubLayout_SingleBoldLineWithTrailingColon_TrimsColonFromTitle()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("The Town Hall:");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        Assert.Equal("The Town Hall", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
    }

    [Fact]
    public void NarrationLayout_SingleBoldLineWithDash_SplitsIntoTitleAndSubtitle()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("YELLOW FEVER - Early Years");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        Assert.Equal("YELLOW FEVER", passages[0].Title);
        Assert.Equal("Early Years", passages[0].Subtitle);
        Assert.Empty(passages[0].Nodes);
    }

    [Fact]
    public void HubLayout_SecondSeparateBoldScope_NeverHoistedAsSubtitle()
    {
        // Regression: Gen1-CreepyTrackRes.mws.yaml and others got a false subtitle from a SECOND,
        // unrelated bold styleScope (e.g. "Carefully hand this storybook to X...") that happened to
        // sit right after a single break following the title's own bold scope. Post-consolidation
        // this reads identically to a genuine two-line heading, so the fix is to never hoist a
        // second bold block at all — only the source's first bold styleScope is ever the heading.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("A Seedy Arrangement");
                }
                StyleScope styleScope1 = null;
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Carefully hand this storybook device to the target player.");
                }
                StyleScope styleScope2 = null;
                yield return base.lineBreak();
                yield break;
            }
            """);

        Assert.Equal("A Seedy Arrangement", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        var body = passages[0].Nodes.OfType<TextNode>().Single();
        Assert.Equal("Carefully hand this storybook device to the target player.", body.Template);
        Assert.Equal("bold", body.Style);
    }

    [Fact]
    public void HubLayout_ThreeSeparateBoldScopes_OnlyFirstHoisted()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Line One");
                }
                StyleScope styleScope1 = null;
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Line Two");
                }
                StyleScope styleScope2 = null;
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Line Three");
                }
                StyleScope styleScope3 = null;
                yield break;
            }
            """);

        Assert.Equal("Line One", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.Equal(["Line Two", "Line Three"], passages[0].Nodes.OfType<TextNode>().Select(t => t.Template));
    }

    [Fact]
    public void IntroductionLayout_TwoBoldLinesInSameScope_HoistsToTitleAndSubtitle()
    {
        // Regression: Scenario5Start.mws.yaml (source lines 2246-2251) is a single
        // `using (styleScope("bold", true))` block containing two text() calls separated by an
        // internal lineBreak() — post-consolidation this looks identical to
        // HubLayout_SecondSeparateBoldScope_NeverHoistedAsSubtitle's TWO SEPARATE scopes, but the
        // break here never leaves the scope, so it must still be hoisted as title+subtitle.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "INTRO" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("GENERATION I:");
                    yield return base.lineBreak();
                    yield return base.text("Yellow Fever");
                }
                StyleScope styleScope = null;
                yield return base.lineBreak();
                yield return base.lineBreak();
                yield return base.text("The siblings' arrival to claim their considerable inheritance...");
                yield break;
            }
            """);

        Assert.Equal("GENERATION I", passages[0].Title);
        Assert.Equal("Yellow Fever", passages[0].Subtitle);
        var body = passages[0].Nodes.OfType<TextNode>().Single();
        Assert.Equal("The siblings' arrival to claim their considerable inheritance...", body.Template);
    }

    [Fact]
    public void Ck2Tag_IsOrdinaryNarration_NoOverlaySynthesized()
    {
        // "ck2" turned out NOT to be the special-event signal (see
        // ShowEventPopupCall_EmitsSpecialEventNodeAtCallSitePosition for the real one) — a
        // "ck2"-tagged passage is just ordinary narration, heading-hoist and all, nothing
        // synthesized into its body just because of the tag.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck2" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("A Bid For Mayor");
                }
                StyleScope styleScope = null;
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        Assert.Equal("narration", passages[0].Layout);
        Assert.Equal("A Bid For Mayor", passages[0].Title);
        Assert.Null(passages[0].Subtitle);

        var textNodes = passages[0].Nodes.OfType<TextNode>().ToList();
        Assert.Single(textNodes);
        Assert.Equal("Body text.", textNodes[0].Template);
    }

    [Fact]
    public void ShowEventPopupCall_EmitsSpecialEventNodeAtCallSitePosition()
    {
        // The real signal: ViewSpecialEvent.instance.ShowEventPopup(), a plain statement (not a
        // yield return — it produces no story output of its own in Cradle). Every real call site
        // in Cost of Disease has an *empty* tags array, so this must be detected in the body, not
        // via any tag. Modeled on the real shape of S5Special1a (the "A Bid for Mayor" mayoral-vote
        // event): a bold title, then the call — not always the very first statement in the method,
        // so the synthesized node must land wherever the call itself sits, not forced to the front.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("A Bid for Mayor");
                }
                StyleScope styleScope = null;
                ViewSpecialEvent.instance.ShowEventPopup();
                yield return base.lineBreak();
                yield return base.text("All players take all their money into their hands.");
                yield break;
            }
            """);

        Assert.Equal("narration", passages[0].Layout);
        Assert.Equal("A Bid for Mayor", passages[0].Title);

        var textNodes = passages[0].Nodes.OfType<TextNode>().ToList();
        Assert.Equal(2, textNodes.Count);
        Assert.Equal("special-event", textNodes[0].Style);
        Assert.Equal("Special Event", textNodes[0].Template);
        Assert.Equal("All players take all their money into their hands.", textNodes[1].Template);
    }

    [Fact]
    public void IntroductionLayout_SingleBoldLineWithDash_SplitsIntoTitleAndSubtitle()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "INTRO" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("A New Beginning - Generation 1");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        Assert.Equal("introduction", passages[0].Layout);
        Assert.Equal("A New Beginning", passages[0].Title);
        Assert.Equal("Generation 1", passages[0].Subtitle);
        Assert.Empty(passages[0].Nodes);
    }

    // ── Random type normalization ──────────────────────────────────────────

    [Fact]
    public void ContiguousIntList_EmitsRandBetween()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.tracker = this.macros1.either(new StoryVar[] { 9, 10, 11 });
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        var rand = effect.VarRandom!["tracker"];
        Assert.Equal("rand-between", rand.RandomType);
        Assert.Equal<int?>(9, rand.Min);
        Assert.Equal<int?>(11, rand.Max);
        Assert.Empty(rand.Values);
    }

    [Fact]
    public void NonContiguousIntList_KeepsChooseOne()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.tracker = this.macros1.either(new StoryVar[] { 2, 5, 8 });
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        var rand = effect.VarRandom!["tracker"];
        Assert.Equal("choose-one", rand.RandomType);
    }

    // ── Switch node ────────────────────────────────────────────────────────

    [Fact]
    public void ConsecutiveSameVarConditionals_EmitsSwitchNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.players == 2) { this.Vars.slots = 3; }
                if (this.Vars.players == 3) { this.Vars.slots = 4; }
                if (this.Vars.players == 4) { this.Vars.slots = 5; }
                yield break;
            }
            """);

        var sw = passages[0].Nodes.OfType<SwitchNode>().First();
        Assert.Equal("players", sw.On);
        Assert.Equal(3, sw.Cases.Count);
        Assert.Equal(2, sw.Cases[0].Match);
        Assert.Equal(3, sw.Cases[1].Match);
        Assert.Equal(4, sw.Cases[2].Match);
    }

    [Fact]
    public void IfElseIfChain_FirstConditionComparesToAnotherVariable_StaysConditionalNotSwitch()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00149-NewMHub.mws.yaml —
        // `if (Vars.trig == Vars.players) {...} else if (Vars.trig == 1) {...} else if (Vars.trig
        // == 2) {...} ...` used to convert to a SwitchNode with `match: 'players'` on the first
        // case — since "players" is a bare (unquoted, non-numeric) identifier, BuildMatchValue
        // silently coerced it into the LITERAL STRING "players", which can never equal an integer
        // `trig`, so the branch that should fire when trig equals the *current value* of `players`
        // never matched and evaluation fell through to `match: 2` instead. A switch's `match:` is
        // always a static literal — a comparison against another variable can't be expressed as one,
        // so the whole if/elseif/else chain must stay a ConditionalNode (correctly evaluated in
        // order) instead of being lossily converted to a switch.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.trig == this.Vars.players)
                {
                    yield return base.abort(goToPassage: "NoUni1");
                }
                else if (this.Vars.trig == 1)
                {
                    yield return base.abort(goToPassage: "NewMaster1B");
                }
                else if (this.Vars.trig == 2)
                {
                    yield return base.abort(goToPassage: "NewMaster1C");
                }
                else if (this.Vars.trig == 3)
                {
                    yield return base.abort(goToPassage: "NewMaster1D");
                }
                else
                {
                    yield return base.abort(goToPassage: "NoUni1");
                }
                yield break;
            }
            """);

        Assert.Empty(passages[0].Nodes.OfType<SwitchNode>());
        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        Assert.Equal(5, cond.Branches.Count);
        Assert.Equal("trig == players", cond.Branches[0].Condition);
        Assert.Equal("trig == 1", cond.Branches[1].Condition);
        Assert.Equal("trig == 2", cond.Branches[2].Condition);
        Assert.Equal("trig == 3", cond.Branches[3].Condition);
        Assert.True(cond.Branches[4].Else);
    }

    [Fact]
    public void ConsecutiveConditionals_OneComparesToAnotherVariable_ExcludedFromSwitch()
    {
        // Same bug, but via the OTHER switch-consolidation path: 2+ consecutive standalone `if`
        // statements on the same variable (not one if/elseif chain) — TryExtractSwitchVar has the
        // identical "bare identifier silently coerced to a literal string" flaw. The first `if`
        // (comparing against another variable) can't itself become a switch case, so it's left
        // behind as its own ConditionalNode; the other two, both genuinely literal, still correctly
        // consolidate into a SwitchNode together — only the specific unsafe branch is excluded.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.trig == this.Vars.players) { this.Vars.slots = 1; }
                if (this.Vars.trig == 2) { this.Vars.slots = 2; }
                if (this.Vars.trig == 3) { this.Vars.slots = 3; }
                yield break;
            }
            """);

        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        Assert.Equal("trig == players", cond.Branches[0].Condition);

        var sw = Assert.Single(passages[0].Nodes.OfType<SwitchNode>());
        Assert.Equal("trig", sw.On);
        Assert.Equal(2, sw.Cases.Count);
        Assert.Equal(2, sw.Cases[0].Match);
        Assert.Equal(3, sw.Cases[1].Match);
    }

    [Fact]
    public void SwitchWithElse_LastConditionalElseBecomesDefault()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.players == 2) { this.Vars.slots = 3; }
                if (this.Vars.players == 3) { this.Vars.slots = 4; }
                else { this.Vars.slots = 6; }
                yield break;
            }
            """);

        var sw = passages[0].Nodes.OfType<SwitchNode>().First();
        Assert.Equal("players", sw.On);
        Assert.Equal(3, sw.Cases.Count);
        Assert.Null(sw.Cases[2].Match);
        Assert.True(sw.Cases[2].Default);
    }

    // ── Break node ─────────────────────────────────────────────────────────

    [Fact]
    public void LineBreak_EmitsBreakNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.lineBreak();
                yield break;
            }
            """);

        Assert.Single(passages[0].Nodes.OfType<BreakNode>());
    }

    [Fact]
    public void DoubleBreak_EmitsParagraphBreak()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("first");
                yield return base.lineBreak();
                yield return base.lineBreak();
                yield return base.text("second");
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        Assert.Contains(nodes, n => n is ParagraphBreakNode);
        Assert.DoesNotContain(nodes, n => n is BreakNode);
        Assert.Equal(2, nodes.OfType<TextNode>().Count());
    }

    [Fact]
    public void TripleBreak_StillEmitsSingleParagraphBreak()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.lineBreak();
                yield return base.lineBreak();
                yield return base.lineBreak();
                yield break;
            }
            """);

        Assert.Single(passages[0].Nodes.OfType<ParagraphBreakNode>());
        Assert.Empty(passages[0].Nodes.OfType<BreakNode>());
    }

    // ── Embedded \n in text() literals ─────────────────────────────────────

    [Fact]
    public void TextWithEmbeddedDoubleNewline_SplitsIntoTwoTextNodesWithParagraphBreak()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("first paragraph.\n\nsecond paragraph");
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        var textNodes = nodes.OfType<TextNode>().ToList();
        Assert.Equal(2, textNodes.Count);
        Assert.Equal("first paragraph.", textNodes[0].Template);
        Assert.Equal("second paragraph", textNodes[1].Template);
        Assert.Single(nodes.OfType<ParagraphBreakNode>());
        Assert.Empty(nodes.OfType<BreakNode>());
    }

    [Fact]
    public void TextWithEmbeddedSingleNewline_SplitsIntoTwoTextNodesWithBreak()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("first line\nsecond line");
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        var textNodes = nodes.OfType<TextNode>().ToList();
        Assert.Equal(2, textNodes.Count);
        Assert.Equal("first line", textNodes[0].Template);
        Assert.Equal("second line", textNodes[1].Template);
        Assert.Single(nodes.OfType<BreakNode>());
        Assert.Empty(nodes.OfType<ParagraphBreakNode>());
    }

    [Fact]
    public void TrailingOnlyNewline_EmitsBreakWithNoEmptyTrailingTextNode()
    {
        // Regression: Cost of Disease's OptiontoKillYes ends its final text() run with a
        // lone `" \n"` immediately before a link() — the space belongs to no visible text
        // (it's leading whitespace on an otherwise-empty trailing segment) and the \n should
        // become just a break between the preceding text and the link, not an empty TextNode.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("Once all players have chosen");
                yield return base.text(" \n");
                yield return base.link("click here to continue", "Next", null);
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        var textNode = Assert.Single(nodes.OfType<TextNode>());
        Assert.Equal("Once all players have chosen", textNode.Template);
        Assert.Single(nodes.OfType<BreakNode>());
        Assert.IsType<LinkNode>(nodes[^1]);
        Assert.IsType<BreakNode>(nodes[^2]);
    }

    [Fact]
    public void EmbeddedNewlineInStringConcatLeaf_SplitsIntoParagraphBreak()
    {
        // Regression, reported against the real Cost of Disease extraction (passage 200,
        // ~line 22969): text()'s argument can be a "prefix" + "suffix" string-concat chain
        // rather than a single literal — routed through ProcessTextConcatPart, a separate
        // code path from ProcessTextInvocation's own plain-literal branch, that had its own
        // unfixed copy of the same newline-stripping bug. The \n\n sits in the second leaf.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("They do not have to pay the cost" +
                    " to open a new plot.\n\nReturn the token to the scenario box.");
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        var textNodes = nodes.OfType<TextNode>().ToList();
        Assert.Equal(2, textNodes.Count);
        Assert.Equal("They do not have to pay the cost to open a new plot.", textNodes[0].Template);
        Assert.Equal("Return the token to the scenario box.", textNodes[1].Template);
        Assert.Single(nodes.OfType<ParagraphBreakNode>());
        Assert.Empty(nodes.OfType<BreakNode>());
    }

    [Fact]
    public void OptiontoKillYesPattern_ThreeTextBlocksTwoParagraphBreaksAndTrailingBreakBeforeLink()
    {
        // Regression, reported against the real Cost of Disease extraction: a run of text()
        // calls merges into a pending buffer as usual, but two of them carry embedded "\n\n"
        // (source lines ~29032/29037) and the last carries a lone trailing "\n" (~29038)
        // immediately before a link(). Previously all of this merged into ONE restext value
        // with the newlines silently collapsed to spaces at restext-write time, losing every
        // paragraph break. Expect 3 separate text blocks, 2 paragraph breaks, and a plain
        // break directly before the link — matching the user's worked-example spec exactly.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Gain 1 Body,");
                }
                StyleScope styleScope = null;
                yield return base.text(" Lose 1");
                yield return base.text(" and Gain 1VP. Then");
                yield return base.text(" they must return a piece to Lost.\n\nIf a player chooses to keep their card,");
                yield return base.text(" they receive no special bonus.\n\nOnce all players have chosen");
                yield return base.text(" \n");
                yield return base.link("click here to continue", "Next", null);
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        var textNodes = nodes.OfType<TextNode>().ToList();
        Assert.Equal(3, textNodes.Count);
        Assert.Equal("**Gain 1 Body,** Lose 1 and Gain 1VP. Then they must return a piece to Lost.", textNodes[0].Template);
        Assert.Equal("If a player chooses to keep their card, they receive no special bonus.", textNodes[1].Template);
        Assert.Equal("Once all players have chosen", textNodes[2].Template);

        Assert.Equal(2, nodes.OfType<ParagraphBreakNode>().Count());
        var plainBreak = Assert.Single(nodes.OfType<BreakNode>());
        var breakIndex = nodes.IndexOf(plainBreak);
        Assert.IsType<LinkNode>(nodes[breakIndex + 1]);
        Assert.Equal(breakIndex, nodes.Count - 2);
    }

    [Fact]
    public void PassageIndex_IsSetFromFunctionNumber()
    {
        var passages = Extract("""
            private void passage42_Init()
            {
                base.Passages["P42"] = new StoryPassage("P42", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage42_Main));
            }
            private IEnumerable<StoryOutput> passage42_Main()
            {
                yield break;
            }
            """);

        Assert.Equal(42, passages[0].PassageIndex);
    }

    // ── Let array and aggregate compute ───────────────────────────────────

    [Fact]
    public void ListDeclaration_EmitsLetArrayNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                List<int> scores = new List<int>(new int[] { this.Vars.scoreA, this.Vars.scoreB, this.Vars.scoreC });
                yield break;
            }
            """);

        var let = passages[0].Nodes.OfType<LetNode>().First();
        Assert.Equal("scores", let.Var);
        Assert.Equal(["scoreA", "scoreB", "scoreC"], let.Array);
    }

    [Fact]
    public void LinqCountIfMax_EmitsMaxLetAndSubstitutesCondition()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                List<int> scores = new List<int>(new int[] { this.Vars.scoreA, this.Vars.scoreB });
                int ties = (from value in scores where value == scores.Max() select value).Count<int>();
                if (ties > 1) { this.Vars.winner = 0; }
                yield break;
            }
            """);

        var lets = passages[0].Nodes.OfType<LetNode>().ToList();
        Assert.Equal(2, lets.Count);
        Assert.Equal("scores", lets[0].Var);
        Assert.Equal("max_scores", lets[1].Var);
        Assert.Equal("max(scores)", lets[1].Compute);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().First();
        Assert.Equal("countif(=max_scores, scores) > 1", cond.Branches[0].Condition);
    }

    [Fact]
    public void MathfMax_SimplifiesInCondition()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.scoreA > Mathf.Max(new int[] { this.Vars.scoreB, this.Vars.scoreC }))
                {
                    this.Vars.winner = this.Vars.nameA;
                }
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().First();
        Assert.Equal("scoreA > max(scoreB, scoreC)", cond.Branches[0].Condition);
    }

    // ── Dynamic passage inclusion ──────────────────────────────────────────

    [Fact]
    public void DynamicPassageVar_EmitsIncludePassageWithVarTarget()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Hunt"] = new StoryPassage("Hunt", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.passage(this.Vars.direction, System.Array.Empty<StoryVar>());
                yield break;
            }
            """);

        var inc = passages[0].Nodes.OfType<IncludePassageNode>().First();
        Assert.Equal("${direction}", inc.Target);
    }

    // ── End of generation node ─────────────────────────────────────────────

    [Fact]
    public void EndOfGenerationPattern_EmitsEogNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Fate1"] = new StoryPassage("Fate1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                string arg = "Remove all player pieces and end the generation.";
                System.Action<string, int> s_OnEndOfGeneration = ViewEndOfGeneration.S_OnEndOfGeneration;
                s_OnEndOfGeneration(arg, 3);
                yield break;
            }
            """);

        var eog = passages[0].Nodes.OfType<EndOfGenerationNode>().First();
        Assert.Equal(3, eog.Generation);
        Assert.Equal("Remove all player pieces and end the generation.", eog.Message);
    }

    [Fact]
    public void EndOfGenerationNullConditionalInvoke_ConvertsRichTextTags()
    {
        // Regression: this is the actual shape Cost of Disease's complete-class source uses
        // (ViewEndOfGeneration.S_OnEndOfGeneration?.Invoke(s, N), not the delegate-variable form
        // above) — a separate code path that assigned the raw message straight to
        // EndOfGenerationNode.Message without ever calling BuildEogMessageTemplate, so <b>/<sprite>
        // tags reached restext completely unconverted (real occurrence: Directive_EndGeneration_
        // DubiousBartering in en-US.common.restext).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Fate1"] = new StoryPassage("Fate1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                string s = "Return all <b>Dubious Bartering</b> cards. Return any remaining <sprite=\"StorybookToken\" index=0> tokens.";
                ViewEndOfGeneration.S_OnEndOfGeneration?.Invoke(s, 3);
                yield break;
            }
            """);

        var eog = passages[0].Nodes.OfType<EndOfGenerationNode>().First();
        Assert.Equal(3, eog.Generation);
        Assert.DoesNotContain("<b>", eog.Message);
        Assert.DoesNotContain("</b>", eog.Message);
        Assert.DoesNotContain("<sprite", eog.Message);
        Assert.Contains("**Dubious Bartering**", eog.Message);
        Assert.Contains("{icon:", eog.Message);
    }

    // ── Array operations ───────────────────────────────────────────────────

    [Fact]
    public void ArrayRemove_EmitsVarRemoveNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.det1 == "visited")
                {
                    this.Vars.effect = this.Vars.effect - this.macros1.a(new StoryVar[] { "DetEffect1" });
                }
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().First();
        var effect = cond.Branches[0].Nodes.OfType<EffectNode>().First();
        Assert.NotNull(effect.VarRemove);
        Assert.Equal("DetEffect1", effect.VarRemove!["effect"]);
    }

    [Fact]
    public void ArrayShuffle_EmitsShuffleExpression()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.effect = this.macros1.shuffled(new StoryVar[] { (HarloweSpread)this.Vars.effect });
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        Assert.NotNull(effect.VarSets);
        Assert.Equal("effect.shuffle()", effect.VarSets!["effect"]);
    }

    [Fact]
    public void ArrayFirst_EmitsFirstExpression()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.tempeffect = this.Vars.effect["1st"];
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        Assert.NotNull(effect.VarSets);
        Assert.Equal("{effect[0]}", effect.VarSets!["tempeffect"]);
    }

    [Fact]
    public void TernaryEither_DecomposesToConditionalNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.let1 = ((this.Vars.players == 3) ? this.Vars.nameC : this.macros1.either(new StoryVar[]
                {
                    this.Vars.nameA,
                    this.Vars.nameB
                }));
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().First();
        Assert.Equal("players == 3", cond.Branches[0].Condition);
        var trueBranch = cond.Branches[0].Nodes.OfType<EffectNode>().First();
        Assert.Equal("{nameC}", trueBranch.VarSets!["let1"]);
        var falseBranch = cond.Branches[1].Nodes.OfType<EffectNode>().First();
        Assert.NotNull(falseBranch.VarRandom);
        Assert.Equal("choose-one", falseBranch.VarRandom!["let1"].RandomType);
    }

    [Fact]
    public void RandomPlusVar_EmitsLetThenVarMath()
    {
        // Regression: this used to emit VarSets with {var}-braced display-template syntax
        // ("{heart} + {_rnd_...}") — invalid inside an expr field, which is always a bare
        // expression, never {}-wrapped (see mws-format-latest.md §4). RestextCollector's
        // "template string" heuristic silently promoted that whole braced string to a restext://
        // key (since it looks exactly like an ordinary display-text template), and at module-load
        // time RestextResolver substitutes the value back in verbatim with the braces still there
        // — the engine's expression parser then chokes on the leading '{' as a malformed record
        // literal. Real-world crash: S5Fate2.mws.yaml's hearttotal assign. Must be VarMath (a bare
        // arithmetic expr) instead, so RestextCollector never sees anything to promote.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.hearttotal = this.macros1.random(1.0, 6.0) + this.Vars.heart;
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        var let = nodes.OfType<LetNode>().First();
        Assert.Equal("range", let.Random!.RandomType);
        Assert.Equal<int?>(1, let.Random.Min);
        Assert.Equal<int?>(6, let.Random.Max);

        var effect = nodes.OfType<EffectNode>().First();
        Assert.Null(effect.VarSets);
        Assert.NotNull(effect.VarMath);
        Assert.True(effect.VarMath!.ContainsKey("hearttotal"));
        var heartotalMath = effect.VarMath["hearttotal"];
        Assert.DoesNotContain("{", heartotalMath);
        Assert.DoesNotContain("}", heartotalMath);
        Assert.Contains("heart", heartotalMath);
        Assert.Contains(let.Var, heartotalMath);
    }

    [Fact]
    public void ThreeOrMoreVarSumChain_EmitsVarMath()
    {
        // Same bug shape as RandomPlusVar_EmitsLetThenVarMath, different code path: a chain of 3+
        // bare variable references summed together used to emit VarSets with a {var}+{var}+{var}
        // braced template, invalid inside an expr field. No known real occurrence in Cost of
        // Disease today, but the fix mirrors the random+var case for consistency.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.total = this.Vars.a + this.Vars.b + this.Vars.c;
                yield break;
            }
            """);

        var effect = passages[0].Nodes.OfType<EffectNode>().First();
        Assert.Null(effect.VarSets);
        Assert.NotNull(effect.VarMath);
        var totalMath = effect.VarMath!["total"];
        Assert.DoesNotContain("{", totalMath);
        Assert.DoesNotContain("}", totalMath);
        Assert.Equal("= a + b + c", totalMath);
    }

    [Fact]
    public void ChainedTernaryEquality_EmitsSwitchNode()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.let1 = ((this.Vars.players == 4) ? this.Vars.nameD : ((this.Vars.players == 3) ? this.macros1.either(new StoryVar[]
                {
                    this.Vars.nameA,
                    this.Vars.nameB,
                    this.Vars.nameC
                }) : this.macros1.either(new StoryVar[]
                {
                    this.Vars.nameA,
                    this.Vars.nameB
                })));
                yield break;
            }
            """);

        var sw = passages[0].Nodes.OfType<SwitchNode>().First();
        Assert.Equal("players", sw.On);
        Assert.Equal(3, sw.Cases.Count);

        Assert.Equal(4, sw.Cases[0].Match);
        var case4 = sw.Cases[0].Nodes.OfType<EffectNode>().First();
        Assert.Equal("{nameD}", case4.VarSets!["let1"]);

        Assert.Equal(3, sw.Cases[1].Match);
        var case3 = sw.Cases[1].Nodes.OfType<EffectNode>().First();
        Assert.Equal("choose-one", case3.VarRandom!["let1"].RandomType);
        Assert.Equal(3, case3.VarRandom["let1"].Values.Count);

        Assert.True(sw.Cases[2].Default);
        var caseDefault = sw.Cases[2].Nodes.OfType<EffectNode>().First();
        Assert.Equal("choose-one", caseDefault.VarRandom!["let1"].RandomType);
        Assert.Equal(2, caseDefault.VarRandom["let1"].Values.Count);
    }

    [Fact]
    public void LogicOnlyGotoPassage_StripsBreaks()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.x = 1;
                yield return base.lineBreak();
                yield return base.abort(this.Vars.target);
                yield return base.lineBreak();
                yield break;
            }
            """);

        Assert.DoesNotContain(passages[0].Nodes, n => n is BreakNode or ParagraphBreakNode);
        Assert.Contains(passages[0].Nodes, n => n is GotoNode);
    }

    // ── --progress-map: layout override ──────────────────────────────────────

    [Fact]
    public void ProgressMap_LayoutEntry_OverridesTagBasedInference()
    {
        var mapper = MakeProgressMapper("""{ "P1": { "layout": "hub_early" } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield break;
            }
            """, mapper, out _);

        Assert.Equal("hub_early", passages[0].Layout);
    }

    [Fact]
    public void ProgressMap_LayoutOverride_StillHoistsHeadingFromUnderlyingTagCategory()
    {
        // Regression: heading-hoist eligibility must key off the tag-based category (hub/
        // narration/introduction), not the final --progress-map-overridden layout value — a
        // "hub" passage overridden to "hub_early" is still fundamentally hub-family and should
        // still get its leading bold line hoisted into Title.
        var mapper = MakeProgressMapper("""{ "P1": { "layout": "hub_early" } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("YELLOW FEVER - Early Years");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """, mapper, out _);

        Assert.Equal("hub_early", passages[0].Layout);
        Assert.Equal("YELLOW FEVER", passages[0].Title);
        Assert.Equal("Early Years", passages[0].Subtitle);
    }

    [Fact]
    public void ProgressMap_PassageNotInMap_KeepsTagBasedInference()
    {
        var mapper = MakeProgressMapper("""{ "SomeOtherPassage": { "layout": "hub_early" } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield break;
            }
            """, mapper, out _);

        Assert.Equal("hub", passages[0].Layout);
    }

    // ── --progress-map: _ProgressRound emission at CheckProgress sites ──────

    [Fact]
    public void CheckProgress_MappedProgressValue_EmitsProgressRoundAssign()
    {
        var mapper = MakeProgressMapper("""{ "P1": { "layout": "hub_early", "progress": 1 } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """, mapper, out _);

        var effect = Assert.Single(passages[0].Nodes.OfType<EffectNode>());
        Assert.Equal(1, effect.VarSets!["_ProgressRound"]);
        Assert.Single(passages[0].Nodes.OfType<CheckProgressNode>());
    }

    [Fact]
    public void CheckProgress_MappedProgressValue_RegistersProgressRoundVariable()
    {
        // A module referencing _ProgressRound (e.g. in layout chrome) would hit an undeclared
        // variable otherwise — this assign is synthesized here, not discovered from any Vars.X
        // reference in the source, so nothing else would register it.
        var tempFile = System.IO.Path.GetTempFileName() + ".cs";
        System.IO.File.WriteAllText(tempFile, """
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """);
        try
        {
            var opts = new ExtractionOptions { InputDir = tempFile, PassagesOutDir = "", IncludeDebug = true };
            var report = new ExtractionReport();
            var mapper = MakeProgressMapper("""{ "P1": { "progress": 1 } }""");
            var extractor = new CradleExtractor(opts, SpriteMapper.Empty(), report, mapper);
            extractor.Extract([tempFile]);

            var vars = extractor.GetDiscoveredVariables();
            Assert.True(vars.ContainsKey("_ProgressRound"));
            Assert.Equal(Masterwork.ModuleFormat.VarKind.Integer, vars["_ProgressRound"].VarType);
        }
        finally { System.IO.File.Delete(tempFile); }
    }

    [Fact]
    public void CheckProgress_ExplicitNullProgress_AcknowledgedAsDeliberateNoOp_EmitsOnlyCheckProgressNode()
    {
        // Explicit "progress": null (present, but deliberately blank) is the map author's way of
        // acknowledging a checkpoint with no value — distinct from omitting "progress" entirely,
        // which still warns (see CheckProgress_PassageNotInMapAtAll_... and the layout-only case
        // right below, neither of which has a "progress" key at all).
        var mapper = MakeProgressMapper("""{ "P1": { "layout": "hub_early", "progress": null } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """, mapper, out var report);

        Assert.Empty(passages[0].Nodes.OfType<EffectNode>());
        Assert.Single(passages[0].Nodes.OfType<CheckProgressNode>());
        Assert.Equal(0, report.UnknownNodeCount);
        Assert.DoesNotContain("no entry in the progress map", WriteReport(report));
    }

    [Fact]
    public void CheckProgress_LayoutOnlyEntryNoProgressKeyAtAll_StillWarns()
    {
        // A layout-only entry (no "progress" key at all — e.g. the real University1OLD/Prosperity3b
        // stray entries in Modules/progress-map.json) isn't a deliberate "no progress" acknowledgment;
        // if a CheckProgress call ever does hit this passage, that's a real gap worth flagging.
        var mapper = MakeProgressMapper("""{ "P1": { "layout": "hub_early" } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """, mapper, out var report);

        Assert.Empty(passages[0].Nodes.OfType<EffectNode>());
        Assert.Single(passages[0].Nodes.OfType<CheckProgressNode>());
        Assert.Contains("no entry in the progress map", WriteReport(report));
    }

    [Fact]
    public void CheckProgress_PassageNotInMapAtAll_WarnsAndEmitsOnlyCheckProgressNode()
    {
        var mapper = MakeProgressMapper("""{ "SomeOtherPassage": { "progress": 1 } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """, mapper, out var report);

        Assert.Empty(passages[0].Nodes.OfType<EffectNode>());
        Assert.Single(passages[0].Nodes.OfType<CheckProgressNode>());
        Assert.Contains("no entry in the progress map", WriteReport(report));
    }

    [Fact]
    public void CheckProgress_NoProgressMapSupplied_BehaviorUnchangedAndNoWarning()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """, out var report);

        Assert.Empty(passages[0].Nodes.OfType<EffectNode>());
        Assert.Single(passages[0].Nodes.OfType<CheckProgressNode>());
        Assert.DoesNotContain("no entry in the progress map", WriteReport(report));
    }

    [Fact]
    public void CheckProgress_MappedEndOfRoundText_ExpandLinkBecomesEndOfRoundPopup()
    {
        // Regression: the reference app shows an acknowledgement popup here (PassageTracker.
        // CheckProgress -> ViewEndOfRound.SetEndOfRound) before navigating on to the next passage —
        // there's no Cradle passage modeling it (ReminderroundEnd is explicitly commented as a
        // prototype-only stand-in never used by the final app logic). Without --progress-map
        // end-of-round text, this is the exact real "click here to continue to the next round..."
        // link + fragment idiom used throughout Cost of Disease (e.g. Fever1 -> FeverServe1); with
        // it, the expand-link must become a popup instead of collapsing to a plain navigation link.
        var mapper = MakeProgressMapper("""
            {
              "P1": {
                "layout": "hub_early",
                "progress": 1,
                "end_of_round_body": "The Early Years has ended.",
                "end_of_round_body2": "Perform all End of Round actions."
              }
            }
            """);
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue to the next round...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """, mapper, out _);

        // Must NOT collapse to a plain navigation LinkNode — that would silently skip the popup.
        Assert.Empty(passages[0].Nodes.OfType<LinkNode>());
        var expand = Assert.Single(passages[0].Nodes.OfType<ExpandLinkNode>());
        Assert.DoesNotContain(expand.ExpandNodes, n => n is CheckProgressNode);
        Assert.DoesNotContain(expand.ExpandNodes, n => n is EffectNode);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("popup", node["type"]);
        Assert.Equal("end_of_round", node["layout"]);
        Assert.Equal("P2", node["target"]);
        Assert.Equal("End of Round", node["okay"]);

        var content = (List<Dictionary<string, object?>>)node["content"]!;
        Assert.Equal("The Early Years has ended.", content[0]["value"]);
        Assert.Equal("break", content[1]["type"]);
        Assert.Equal("paragraph", content[1]["style"]);
        Assert.Equal("Perform all End of Round actions.", content[2]["value"]);

        var onclose = (List<Dictionary<string, object?>>)node["onclose"]!;
        var assign = Assert.Single(onclose);
        Assert.Equal("assign", assign["type"]);
        Assert.Equal("_ProgressRound", assign["var"]);
        Assert.Equal("1", assign["expr"]);
    }

    [Fact]
    public void CheckProgress_ConditionalWithSameCurrentPassage_CollapsesToSingleCheckpointWithTernaryTarget()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00037-NoUni3.mws.yaml. The
        // fragment's whole body can be an if/else whose branches BOTH call CheckProgress with the
        // same current-passage argument but a different target — `if (peeps == 1) CheckProgress
        // ("P1", "P2"); else CheckProgress("P1", "P3");`. Previously StitchFragments only recognized
        // a bare terminal GotoNode/CheckProgressNode, so this fell through untouched: the popup got
        // no target/okay/layout at all, and its content was the raw conditional with only the
        // _ProgressRound assigns left in it (CheckProgressNode silently dropped by V2Serializer,
        // which only ever consumes it as a StitchFragments terminal). Since both branches report the
        // SAME current passage, this must collapse to ONE end_of_round popup — one copy of the
        // curated body text, one onclose assign — whose target is a ternary expression choosing
        // between the branches' individual target passages.
        var mapper = MakeProgressMapper("""
            {
              "P1": {
                "layout": "hub_late",
                "progress": 9,
                "end_of_round_body": "The Late Years has ended.",
                "end_of_round_body2": "Perform any End of Round actions before continuing."
              }
            }
            """);
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                if (this.Vars.peeps == 1)
                {
                    PassageTracker.instance.CheckProgress("P1", "P2");
                }
                else
                {
                    PassageTracker.instance.CheckProgress("P1", "P3");
                }
                yield break;
            }
            """, mapper, out _);

        Assert.Empty(passages[0].Nodes.OfType<LinkNode>());
        var expand = Assert.Single(passages[0].Nodes.OfType<ExpandLinkNode>());
        Assert.DoesNotContain(expand.ExpandNodes, n => n is ConditionalNode);
        Assert.DoesNotContain(expand.ExpandNodes, n => n is CheckProgressNode);
        Assert.DoesNotContain(expand.ExpandNodes, n => n is EffectNode);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("popup", node["type"]);
        Assert.Equal("end_of_round", node["layout"]);
        Assert.Equal("${peeps == 1 ? \"P2\" : \"P3\"}", node["target"]);
        Assert.Equal("End of Round", node["okay"]);

        // Exactly one copy of the curated body — not one per conditional branch.
        var content = (List<Dictionary<string, object?>>)node["content"]!;
        Assert.Equal(3, content.Count);
        Assert.Equal("The Late Years has ended.", content[0]["value"]);
        Assert.Equal("Perform any End of Round actions before continuing.", content[2]["value"]);

        var onclose = (List<Dictionary<string, object?>>)node["onclose"]!;
        var assign = Assert.Single(onclose);
        Assert.Equal("_ProgressRound", assign["var"]);
        Assert.Equal("9", assign["expr"]);
    }

    [Fact]
    public void CheckProgress_TernaryTargetArgument_BecomesExpressionTarget()
    {
        // Regression, found via a survey of the other two module sources: A Time of War's
        // MonarchReign2 calls CheckProgress with a single ternary expression directly as the
        // target argument (not two separate calls in if/else branches, unlike NoUni3/Warning2time)
        // — CheckProgress("MonarchReign2", Vars.advisorcount >= Vars.commtrigger ?
        // "CommemorativeStatueEvent" : "MonarchReign3"). GetStringValue can't resolve a ternary to
        // a literal, so TargetPassage used to come out empty, leaving the popup with no target/
        // okay/layout — same broken shape as the other two patterns.
        var mapper = MakeProgressMapper("""{ "P1": { "progress": 1 } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                PassageTracker.instance.CheckProgress("P1", this.Vars.advisorcount >= this.Vars.commtrigger ? "P2" : "P3");
                yield break;
            }
            """, mapper, out _);

        // No curated end-of-round text for P1 — falls to a plain LinkNode, not a popup.
        Assert.Empty(passages[0].Nodes.OfType<ExpandLinkNode>());
        var link = Assert.Single(passages[0].Nodes.OfType<LinkNode>());
        Assert.Equal("${advisorcount >= commtrigger ? \"P2\" : \"P3\"}", link.Target);
        Assert.True(link.StateAffecting);
    }

    [Fact]
    public void CheckProgress_TargetIsUnresolvedSessionVariable_TargetsItDirectlyAndRunsPriorLogicAsOnclose()
    {
        // Regression, found via a survey of the other two module sources: Fear of the Unknown's
        // Liberal2 has three fragments; two of them guard-assign a SESSION variable
        // (Vars.Liberal2nextpsg, via macros1.either()) just before calling CheckProgress with that
        // variable as the target — not a local C# var (already handled by _localVars/
        // _localPassageConditionals) and not a literal, so TargetPassage used to come out empty.
        // Per the user's explicit design: the target becomes "${Liberal2nextpsg}" directly (a
        // session variable is always a valid expression), and the guard+assign that computes it
        // must run as onclose (right before target resolves), not passage-render-time content.
        var mapper = MakeProgressMapper("""
            {
              "P1": {
                "progress": 1,
                "end_of_round_body": "The round has ended."
              }
            }
            """);
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                if (this.Vars.nextpsg == "" || this.Vars.nextpsg == 0)
                {
                    this.Vars.nextpsg = macros1.either("P2", "P3");
                }
                PassageTracker.instance.CheckProgress("P1", this.Vars.nextpsg);
                yield break;
            }
            """, mapper, out _);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("popup", node["type"]);
        Assert.Equal("end_of_round", node["layout"]);
        Assert.Equal("${nextpsg}", node["target"]);

        // The guard conditional must run as onclose (before the _ProgressRound assign / target
        // resolution), and content must be ONLY the curated body — the guard must not leak into it.
        var onclose = (List<Dictionary<string, object?>>)node["onclose"]!;
        Assert.Equal(2, onclose.Count);
        Assert.Equal("conditional", onclose[0]["type"]);
        Assert.Equal("_ProgressRound", onclose[1]["var"]);

        var content = (List<Dictionary<string, object?>>)node["content"]!;
        var textContent = Assert.Single(content);
        Assert.Equal("The round has ended.", textContent["value"]);
    }

    [Fact]
    public void LinkTarget_TernaryExpression_EmitsOneLinkHintPerConstant()
    {
        // Regression: link.target (and by extension popup.target, goto.target, etc. — all routed
        // through the same AddLinkHint) previously only ever emitted a file-path comment for a
        // plain literal target, skipping any "${...}" expression target outright — including a
        // ternary whose branches are themselves just string literals, where the destination IS
        // still statically knowable per-branch (e.g. A Time of War's MonarchReign2).
        var mapper = MakeProgressMapper("""{ "P1": { "progress": 1 } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                PassageTracker.instance.CheckProgress("P1", this.Vars.advisorcount >= this.Vars.commtrigger ? "P2" : "P3");
                yield break;
            }
            """, mapper, out _);

        var ctx = new SerializationContext(SourceRelativePath: null, PassageFileMap: new Dictionary<string, string>
        {
            ["P2"] = "./0002-P2.mws.yaml",
            ["P3"] = "./0003-P3.mws.yaml",
        });
        var dict = V2Serializer.ToDict(passages[0], ctx);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];

        Assert.Equal("${advisorcount >= commtrigger ? \"P2\" : \"P3\"}", node["target"]);
        Assert.Equal("./0002-P2.mws.yaml, ./0003-P3.mws.yaml", node["_link"]);

        // _link must sit immediately after target (before snapshot) — see the analogous popup
        // ordering assertion below for why this isn't just cosmetic.
        var keys = node.Keys.ToList();
        Assert.Equal(keys.IndexOf("target") + 1, keys.IndexOf("_link"));
    }

    [Fact]
    public void PopupTarget_TernaryExpression_EmitsOneLinkHintPerConstant()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00037-NoUni3.mws.yaml — the
        // end_of_round popup's target ('${peeps == 1 ? "NoUni3b" : "Scoring"}') got no file-path
        // comment at all, unlike an ordinary link's literal target. Same AddLinkHint fix as the
        // plain-link case above, exercised through the popup path (EndOfRoundMarkerNode) instead.
        var mapper = MakeProgressMapper("""
            {
              "P1": {
                "progress": 1,
                "end_of_round_body": "The round has ended."
              }
            }
            """);
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                if (this.Vars.peeps == 1)
                {
                    PassageTracker.instance.CheckProgress("P1", "NoUni3b");
                }
                else
                {
                    PassageTracker.instance.CheckProgress("P1", "Scoring");
                }
                yield break;
            }
            """, mapper, out _);

        var ctx = new SerializationContext(SourceRelativePath: null, PassageFileMap: new Dictionary<string, string>
        {
            ["NoUni3b"] = "./0040-NoUni3b.mws.yaml",
            ["Scoring"] = "./0284-Scoring.mws.yaml",
        });
        var dict = V2Serializer.ToDict(passages[0], ctx);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];

        Assert.Equal("popup", node["type"]);
        Assert.Equal("${peeps == 1 ? \"NoUni3b\" : \"Scoring\"}", node["target"]);
        Assert.Equal("./0040-NoUni3b.mws.yaml, ./0284-Scoring.mws.yaml", node["_link"]);

        // _link must sit immediately after target in the dict's key order — InjectSentinelComments
        // (Program.cs) attaches the hint to whichever line precedes it in the emitted YAML, keyed
        // purely off insertion order, so an intervening key (okay/onclose) would land the comment
        // on the wrong line even though the dict's *value* for "_link" itself is already correct.
        var keys = node.Keys.ToList();
        Assert.Equal(keys.IndexOf("target") + 1, keys.IndexOf("_link"));
    }

    [Fact]
    public void PopupTarget_PlainLiteralTarget_LinkHintKeyImmediatelyFollowsTarget()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00002-Fever1.mws.yaml — even a
        // plain literal end_of_round popup target (the overwhelmingly common case — every
        // occurrence before NoUni3/Warning2time's conditional pattern) got no file-path comment,
        // because TransformPopup's eorMarker branch called AddLinkHint AFTER inserting `okay` and
        // `onclose` into the dict, landing the hint on the onclose block's last line instead of
        // the target line. This predates the "${...}"-expression-target work entirely — that work
        // only made it visible by being the first thing to actually populate `_link` for this
        // branch (AddLinkHint used to unconditionally skip "${"-prefixed targets, and every other
        // target here was a plain literal, so the misplacement was never exercised... except it
        // was, silently, for every plain-literal case too. This test pins the fix for both.
        var mapper = MakeProgressMapper("""
            {
              "P1": {
                "progress": 1,
                "end_of_round_body": "The round has ended."
              }
            }
            """);
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                PassageTracker.instance.CheckProgress("P1", "P2");
                yield break;
            }
            """, mapper, out _);

        var ctx = new SerializationContext(SourceRelativePath: null, PassageFileMap: new Dictionary<string, string>
        {
            ["P2"] = "./0002-P2.mws.yaml",
        });
        var dict = V2Serializer.ToDict(passages[0], ctx);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];

        Assert.Equal("P2", node["target"]);
        Assert.Equal("./0002-P2.mws.yaml", node["_link"]);

        var keys = node.Keys.ToList();
        Assert.Equal(keys.IndexOf("target") + 1, keys.IndexOf("_link"));
    }

    [Fact]
    public void PopupTarget_BareVariableExpression_EmitsNoLinkHint()
    {
        // A target expression with no literal substrings at all (e.g. a bare session-variable
        // reference) has no statically-knowable destination — must not emit a hint, let alone a
        // wrong one, since AddLinkHint doesn't trace the variable back to its assignment sites.
        var mapper = MakeProgressMapper("""
            {
              "P1": {
                "progress": 1,
                "end_of_round_body": "The round has ended."
              }
            }
            """);
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                PassageTracker.instance.CheckProgress("P1", this.Vars.nextpsg);
                yield break;
            }
            """, mapper, out _);

        var ctx = new SerializationContext(SourceRelativePath: null, PassageFileMap: new Dictionary<string, string>
        {
            ["NoUni3b"] = "./0040-NoUni3b.mws.yaml",
        });
        var dict = V2Serializer.ToDict(passages[0], ctx);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];

        Assert.Equal("${nextpsg}", node["target"]);
        Assert.False(node.ContainsKey("_link"));
    }

    [Fact]
    public void ExpandLink_AssignThenExhaustiveIfElseOfGotos_BecomesLinkWithOnclick()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00130-HospitalVisitCheck.mws.yaml.
        // The Cradle idiom here is a link(...) -> enchantHook fragment whose body is an assign
        // followed by an if/elseif/.../else chain where EVERY branch ends in abort(goToPassage:...).
        // This isn't IsNavigationOnly (the leading assign is an EffectNode), but it's still guaranteed
        // to hit a goto by the time it finishes, so it must become a `link` with `onclick` (and no
        // `target`) rather than a `popup` with no way to close it.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Dr. Smith Jr.", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                this.Vars.hospsign = this.Vars.nameA;
                if (this.Vars.hospsign == this.Vars.nameA && this.Vars.hospA == "yes")
                {
                    yield return base.abort(goToPassage: "HospitalVisitReject");
                }
                else if (this.Vars.hospsign == this.Vars.nameB && this.Vars.hospB == "yes")
                {
                    yield return base.abort(goToPassage: "HospitalVisitReject");
                }
                else
                {
                    yield return base.abort(goToPassage: "HospitalVisitCheck2");
                }
                yield break;
            }
            """);

        // Extractor-internal node types stay unchanged (V2Serializer does the transform at output time).
        Assert.Empty(passages[0].Nodes.OfType<LinkNode>());
        Assert.Single(passages[0].Nodes.OfType<ExpandLinkNode>());

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("link", node["type"]);
        Assert.Equal("Dr. Smith Jr.", node["label"]);
        Assert.False(node.ContainsKey("target"));

        var onclick = (List<Dictionary<string, object?>>)node["onclick"]!;
        Assert.Equal("assign", onclick[0]["type"]);
        Assert.Equal("hospsign", onclick[0]["var"]);
        Assert.Equal("conditional", onclick[1]["type"]);

        var conditions = (List<Dictionary<string, object?>>)onclick[1]["conditions"]!;
        var firstThen = (List<Dictionary<string, object?>>)conditions[0]["then"]!;
        Assert.Equal("goto", firstThen[0]["type"]);
        Assert.Equal("HospitalVisitReject", firstThen[0]["target"]);

        var elseNodes = (List<Dictionary<string, object?>>)onclick[1]["else"]!;
        Assert.Equal("goto", elseNodes[0]["type"]);
        Assert.Equal("HospitalVisitCheck2", elseNodes[0]["target"]);
    }

    [Fact]
    public void ExpandLink_AssignThenNonExhaustiveConditionalOfGotos_StaysPopup()
    {
        // Guardrail: without a final `else`, the conditional isn't provably exhaustive, so a link
        // relying solely on a goto inside onclick could do nothing on a click that matches no
        // branch. Must still become a popup in that case (a separate, pre-existing concern that
        // this popup has no `okay` is out of scope for this fix).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Dr. Smith Jr.", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                this.Vars.hospsign = this.Vars.nameA;
                if (this.Vars.hospsign == this.Vars.nameA && this.Vars.hospA == "yes")
                {
                    yield return base.abort(goToPassage: "HospitalVisitReject");
                }
                yield break;
            }
            """);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("popup", node["type"]);
    }

    [Fact]
    public void InlineEitherInConditional_HoistsToLetBeforeConditional()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00131-HospitalVisitCheck2.mws.yaml.
        // `if (macros1.either(1, 2) == 1)` used to pass the literal C# call straight through into
        // the emitted `if:` expression untouched, since SimplifyCondition only does textual Vars.X
        // normalization — the engine then failed at render time with "Unknown variable 'macros1'".
        // Cradle draws a fresh value every either() call, so it must be hoisted into its own `let`
        // right before the conditional (mirroring how an either() on an assignment's RHS already
        // becomes a VarRandom), not left inline in the `if:` expression.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.hospentry == 1)
                {
                    if (macros1.either(1, 2) == 1)
                    {
                        yield return base.text("branch A");
                    }
                    else
                    {
                        yield return base.text("branch B");
                    }
                }
                yield break;
            }
            """);

        var outer = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        var thenNodes = outer.Branches.Single(b => b.Else != true).Nodes;

        var let = Assert.Single(thenNodes.OfType<LetNode>());
        Assert.NotNull(let.Random);
        Assert.DoesNotContain("either", let.Var);

        var inner = Assert.Single(thenNodes.OfType<ConditionalNode>());
        var innerCond = inner.Branches.Single(b => b.Else != true).Condition!;
        Assert.DoesNotContain("either", innerCond);
        Assert.DoesNotContain("macros1", innerCond);
        Assert.Contains(let.Var, innerCond);

        var dict = V2Serializer.ToDict(passages[0]);
        var nodes = (List<Dictionary<string, object?>>)dict["nodes"]!;
        var outerThen = (List<Dictionary<string, object?>>)nodes.Single(n => (string)n["type"]! == "conditional")["then"]!;
        var letDict = Assert.Single(outerThen, n => (string)n["type"]! == "let");
        Assert.Equal("rand_between(1, 2, \"P1_0\")", letDict["expr"]);

        var innerCondDict = Assert.Single(outerThen, n => (string)n["type"]! == "conditional");
        // A single if/else pair flattens to if/then/else directly — no `conditions:` wrapper.
        var innerIf = (string)innerCondDict["if"]!;
        Assert.DoesNotContain("either", innerIf);
        Assert.DoesNotContain("macros1", innerIf);
        Assert.Contains(let.Var, innerIf);
    }

    // ── ViewController.instance.ChangeView(...) ──────────────────────────────

    [Fact]
    public void ChangeViewScoreEntry_ExpandLinkBecomesPlainNavigationToScoreEntry()
    {
        // Regression: Cost of Disease's Scoring passage (source ~line 3029-3036) hands off to
        // the reference app's native score-entry UI via ViewController.instance.ChangeView(
        // ViewController.instance.scoreEntry). IsChangeViewMainMenu used to match ANY ChangeView(
        // ...) call by method name alone, mislabeling this as GotoMenuNode { Target = "main_menu"
        // } — which V2Serializer then silently drops, losing the navigation to score entry
        // entirely. Every real story-script ChangeView(...) call site actually passes
        // .scoreEntry (confirmed via cross-module grep); this must become a plain navigation
        // link to the fixed "ScoreEntry" passage id instead.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h00006"))
                    yield return base.link("Click to tabulate scores for posterity...", null, () => base.enchantHook("h00006", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                ViewController.instance.ChangeView(ViewController.instance.scoreEntry);
                yield return base.lineBreak();
                yield break;
            }
            """);

        Assert.Empty(passages[0].Nodes.OfType<ExpandLinkNode>());
        var link = Assert.Single(passages[0].Nodes.OfType<LinkNode>());
        Assert.Equal("ScoreEntry", link.Target);
    }

    [Fact]
    public void ChangeViewMainMenu_StillEmitsGotoMenuNode()
    {
        // Defensive case: .mainMenu never actually occurs in any story-script source, but should
        // stay correct/cheap to keep.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                ViewController.instance.ChangeView(ViewController.instance.mainMenu);
                yield break;
            }
            """);

        var menuNode = Assert.Single(passages[0].Nodes.OfType<GotoMenuNode>());
        Assert.Equal("main_menu", menuNode.Target);
    }

    private static string WriteReport(ExtractionReport report)
    {
        var tempReportPath = System.IO.Path.GetTempFileName();
        try
        {
            report.Write(tempReportPath);
            return System.IO.File.ReadAllText(tempReportPath);
        }
        finally
        {
            System.IO.File.Delete(tempReportPath);
        }
    }
}
