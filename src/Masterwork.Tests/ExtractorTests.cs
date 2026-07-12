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
    public void BoldStyleScope_AppliesBoldStyle()
    {
        // Tagged "ck2" (event layout) so this leading bold text isn't intercepted by the
        // hub/narration heading-hoist (TryHoistHeadingTitleSubtitle) — this test is about bold
        // style application, not the heading feature; see HeadingHoist tests for that.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck2" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
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
        // Tagged "ck2" (event layout) — see BoldStyleScope_AppliesBoldStyle's comment.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck2" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
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
    public void HubLayout_TwoBoldLinesSeparatedByBreak_HoistsEachLineSeparately()
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
                    yield return base.text("The Good Fight");
                }
                StyleScope styleScope1 = null;
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Early Years");
                }
                StyleScope styleScope2 = null;
                yield break;
            }
            """);

        Assert.Equal("The Good Fight", passages[0].Title);
        Assert.Equal("Early Years", passages[0].Subtitle);
        Assert.Empty(passages[0].Nodes);
    }

    [Fact]
    public void HubLayout_ThreeBoldLines_DoesNotHoist()
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

        Assert.Equal("P1", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.Equal(5, passages[0].Nodes.Count);
    }

    [Fact]
    public void EventLayout_LeadingBoldLine_DoesNotHoist()
    {
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "ck2" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Not A Heading");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        Assert.Equal("P1", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.Single(passages[0].Nodes.OfType<TextNode>());
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
    public void RandomPlusVar_EmitsLetThenVarSets()
    {
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
        Assert.NotNull(effect.VarSets);
        Assert.True(effect.VarSets!.ContainsKey("hearttotal"));
        var heartotalVal = (string)effect.VarSets["hearttotal"]!;
        Assert.Contains("{heart}", heartotalVal);
        Assert.Contains(let.Var, heartotalVal);
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
