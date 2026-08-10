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
    public void ConcatenatedBoldTagFollowedByVariable_PreservesTrailingSpace()
    {
        // Regression: Fear of the Unknown's LiberalEvent2a_003 - "...offered. " +
        // "<b>If you have collectively met or exceeded " is one text() call ("+"-concatenated),
        // immediately followed by a separate text(Vars.bribed) call. The second literal leaf has no
        // <sprite=...> tag, so TryParseRichText's "no sprite tag matched" path (pos stays 0 the
        // whole way through) used to run an unconditional .Trim() on the WHOLE segment regardless
        // of what followed it — eating the trailing space that was meant to separate "exceeded"
        // from the {bribed} variable run right after it, producing "...exceeded{bribed}," instead
        // of "...exceeded {bribed},".
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("Count up all coins offered. " +
                    "<b>If you have collectively met or exceeded ");
                yield return base.text(this.Vars.bribed);
                yield return base.text(", the bribe succeeds</b> and all players pay.");
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First();
        Assert.Contains("exceeded {bribed}", textNode.Template);
    }

    [Fact]
    public void LeadingSpaceBeforeHtmlTag_StillTrimmed()
    {
        // Contrast case for the fix above: the LEADING edge of a rich-text segment must still be
        // trimmed even when there's no sprite tag anywhere in it. Regression: Fear of the Unknown's
        // BusMeetA-E each have `link("I'm not convinced.", ...); text(" <i>This option will provide
        // a fifty percent chance...</i>")` - the leading space before <i> is leftover Cradle
        // formatting noise with nothing in the SAME text() call to attach to. An early version of
        // the trailing-space fix above went too far and stopped trimming ANY edge whenever no sprite
        // tag was present, which introduced a stray leading space here — breaking the curated
        // Common_NNN restext match against .source/en-US.common.restext (an exact-text lookup) and
        // knocking this string back to an auto-numbered id.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h1"))
                    yield return base.link("I'm not convinced.", null, () => base.enchantHook("h1", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield return base.text(" <i>This option will provide a fifty percent chance of improving the deal.</i>");
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().Last();
        Assert.Equal("_This option will provide a fifty percent chance of improving the deal._", textNode.Template);
    }

    [Fact]
    public void BoldStyleScope_AppliesBoldStyle()
    {
        // A leading plain (non-bold) sentence — not a bare Vars assignment — keeps this bold text
        // out of the hub/narration heading-hoist: TryHoistHeadingTitleSubtitle now also skips a
        // leading run of heading-*inert* nodes (assigns, breaks, auto-display popups) to find a
        // leading bold heading behind them (see the Player1Stats-style HeadingHoist tests), so a
        // bare assign no longer works as an escape hatch here — but real, non-inert body text
        // does, since the flat shape-1/shape-2 check only ever looks at what's genuinely first.
        // This test is about bold style application, not the heading feature; see HeadingHoist
        // tests for that.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("Plain lead-in.");
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Bold text");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        var textNode = passages[0].Nodes.OfType<TextNode>().First(t => t.Style == "bold");
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

    [Fact]
    public void SetupImageAssignment_NeverRegisteredAsDiscoveredVariable()
    {
        // Regression: survey2 review found `_SetupImage` sitting in every module's _variables.yaml
        // with no apparent way to be assigned in the extracted output — because there isn't one.
        // `Vars._SetupImage` is a real Vars.X access syntactically, so variable discovery
        // (Pass1_DiscoverVariables, which scans raw Vars.X syntax independently of how
        // ProcessAssignment ultimately handles the node) registered it anyway, even though
        // ProcessAssignment always converts it into a popup header ImageNode instead of a real
        // assign — it never surfaces as `{_SetupImage}` in any template or `if:` condition anywhere
        // downstream, so an engine-tracked session variable for it is pure dead weight.
        var tempFile = System.IO.Path.GetTempFileName() + ".cs";
        System.IO.File.WriteAllText(tempFile, """
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
        try
        {
            var opts = new ExtractionOptions { InputDir = tempFile, PassagesOutDir = "", IncludeDebug = true };
            var report = new ExtractionReport();
            var extractor = new CradleExtractor(opts, SpriteMapper.Empty(), report, ProgressMapper.Empty());
            extractor.Extract([tempFile]);

            var vars = extractor.GetDiscoveredVariables();
            Assert.False(vars.ContainsKey("_SetupImage"));
        }
        finally { System.IO.File.Delete(tempFile); }
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
        // See BoldStyleScope_AppliesBoldStyle's comment — a leading, non-inert plain sentence (not
        // a bare assign, which the heading-hoist now skips right past to find a bold heading behind
        // it) keeps this bold text out of the hub/narration heading-hoist.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.text("Plain lead-in.");
                yield return base.lineBreak();
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

        var textNode = passages[0].Nodes.OfType<TextNode>().First(t => t.Style == "bold");
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
    public void NarrationLayout_RootConditionalWithOneContentBranchAndOneGotoSkipBranch_HoistsFromContentBranch()
    {
        // Regression: A Time of War's TSBarracksPenalty passage (source line 20057) — the reference
        // app's own title/subtitle promotion still applies whenever this passage actually renders,
        // but TryHoistHeadingTitleSubtitle only ever looked at the FLAT top-level node list; here
        // the whole body is one root "if (barracks == "yes") { <bold heading + content> } else {
        // goto SomewhereElse; }" idiom Cradle uses to make an entire optional passage a no-op when
        // its guard doesn't hold — the bold heading is one level down, inside the branch that
        // actually renders, so the flat check never saw it and no title was ever emitted.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.barracks == "yes")
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Lack of Service Penalty");
                    }
                    yield return base.lineBreak();
                    yield return base.lineBreak();
                    yield return base.text("If any player's Caretaker remains in the barracks...");
                }
                else
                {
                    yield return base.abort(goToPassage: "SeedGUNS");
                }
                yield break;
            }
            """);

        Assert.Equal("Lack of Service Penalty", passages[0].Title);
        Assert.Null(passages[0].Subtitle);

        // Exactly one root ConditionalNode remains — the heading was stripped from its `then`
        // branch only, the `else` (goto-only) branch untouched.
        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        var thenBranch = cond.Branches.Single(b => b.Else != true);
        var elseBranch = cond.Branches.Single(b => b.Else == true);
        Assert.DoesNotContain(thenBranch.Nodes, n => n is TextNode { Style: "bold" });
        var remainingText = Assert.Single(thenBranch.Nodes.OfType<TextNode>());
        Assert.Equal("If any player's Caretaker remains in the barracks...", remainingText.Template);
        Assert.Single(elseBranch.Nodes.OfType<GotoNode>());
    }

    [Fact]
    public void NarrationLayout_RootConditionalBothBranchesHaveHeadings_CollapsesToTernaryTitle()
    {
        // When EVERY branch of a root conditional has its own bold heading (not just one, with the
        // rest skip-only), there's no single STATIC title — but title/subtitle's "{...}" template
        // placeholder already evaluates a full expression (ternaries included, same evaluator as
        // "${...}"), so the extractor can collapse per-branch headings into one ternary-chained
        // title, the same way BuildTernaryChain already does for target/goto. Real-world shape:
        // A Time of War's SeedGUNS (a switch, not a plain if/else — see the SwitchNode-shape sibling
        // test — but the same collapse applies to a plain if/else conditional too).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.flag == "yes")
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Heading A");
                    }
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Heading B");
                    }
                }
                yield break;
            }
            """);

        Assert.Equal("{flag == \"yes\" ? \"Heading A\" : \"Heading B\"}", passages[0].Title);
        Assert.Null(passages[0].Subtitle);

        // The heading was stripped from BOTH branches this time (not just one).
        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        Assert.All(cond.Branches, b => Assert.DoesNotContain(b.Nodes, n => n is TextNode { Style: "bold" }));
        Assert.All(cond.Branches, b => Assert.Empty(b.Nodes));
    }

    [Fact]
    public void NarrationLayout_RootSwitchEveryCaseHasHeading_CollapsesToTernaryTitle()
    {
        // Regression: A Time of War's SeedGUNS passage (source line 14190) — after a couple of
        // if-blocks with assigns (condensed to a switch), the rest of the passage is a switch on
        // gunsbonus with 1/2/3/default cases, EACH starting with its own bold heading ("Knowledge
        // Bonus"/"Ingredient Bonus"/"Wealth Bonus"/"Knowledge Bonus"). TryHoistFromOneBranch only
        // fires when exactly one case has heading content; here every case does, so nothing was
        // ever hoisted and the passage had no title at all. Fixed by collapsing every case's own
        // heading into one ternary-chained title, matching the switch's own on/match/default shape
        // (BuildSwitchCaseCondition rebuilds an equivalent boolean condition per case).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.gunsbonus == 1)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Knowledge Bonus");
                    }
                }
                else if (this.Vars.gunsbonus == 2)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Ingredient Bonus");
                    }
                }
                else if (this.Vars.gunsbonus == 3)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Wealth Bonus");
                    }
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Knowledge Bonus");
                    }
                }
                yield break;
            }
            """);

        Assert.Equal(
            "{gunsbonus == 1 ? \"Knowledge Bonus\" : gunsbonus == 2 ? \"Ingredient Bonus\" : gunsbonus == 3 ? \"Wealth Bonus\" : \"Knowledge Bonus\"}",
            passages[0].Title);
        Assert.Null(passages[0].Subtitle);

        var sw = Assert.Single(passages[0].Nodes.OfType<SwitchNode>());
        Assert.All(sw.Cases, c => Assert.DoesNotContain(c.Nodes, n => n is TextNode { Style: "bold" }));
        Assert.All(sw.Cases, c => Assert.Empty(c.Nodes));
    }

    [Fact]
    public void NarrationLayout_LeadingInertSwitchThenHeadingSwitch_StillCollapsesToTernaryTitle()
    {
        // SeedGUNS's actual shape (source lines 14192-14290): two SEPARATE sibling `if` statements
        // (no `else`, each assigning `war`) get consolidated into a purely "invisible" switch
        // (every case is just an assign, no heading of its own) that precedes the heading-bearing
        // switch — the title-bearing switch is neither the only top-level node NOR the last one:
        // three more lineBreak() calls follow it before yield break. IsHeadingInert must recognize
        // both the leading switch and the trailing breaks as safe to skip past.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.townname == "Paradox")
                {
                    this.Vars.war = 1;
                }
                if (this.Vars.townname == "Destruction")
                {
                    this.Vars.war = 2;
                }
                if (this.Vars.gunsbonus == 1)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Knowledge Bonus");
                    }
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Ingredient Bonus");
                    }
                }
                yield return base.lineBreak();
                yield return base.lineBreak();
                yield return base.lineBreak();
                yield break;
            }
            """);

        Assert.Equal("{gunsbonus == 1 ? \"Knowledge Bonus\" : \"Ingredient Bonus\"}", passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_LeadingInputPromptThenHeading_StillHoistsTitle()
    {
        // Regression: Fear of the Unknown's Player1Stats..Player5Stats, each starting with a
        // conditional-gated input-prompt popup (collapsed to a bare InputPromptNode, not literally a
        // ConditionalNode — see IsInputPromptIf) before its own "**Agility Confirmed**"-style bold
        // heading. The flat shape-1/shape-2 heading check only ever looked at nodes[0] itself, so an
        // InputPromptNode sitting in front of the real heading defeated the hoist entirely — unlike
        // the ConditionalNode/SwitchNode-sole-candidate path (see the previous test), which already
        // tolerates inert siblings. IsHeadingInert now also recognizes InputPromptNode/
        // EndOfGenerationNode (same "separate overlay, not passage body text" reasoning as
        // BreakFilter's own IsNonRendered), and the flat check now skips any leading inert run too.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.PassageValueNumber() >= 0)
                {
                    this.Vars.agiA = this.PassageValueNumber();
                }
                else
                {
                    this.OnGenerationBtn("agiA", "Enter your VP", "number");
                }
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Agility Confirmed");
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        Assert.Equal("Agility Confirmed", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.Single(passages[0].Nodes.OfType<InputPromptNode>());
        Assert.DoesNotContain(passages[0].Nodes, n => n is TextNode t && t.Template == "Agility Confirmed");
    }

    [Fact]
    public void NarrationLayout_HeadingBoldTextDependsOnPrecedingLet_StillHoistsAsTernary()
    {
        // Regression/correction: Cost of Disease's HuntSuccessCheck (source lines 32632-32639) —
        // each branch of `if (huntresult == "success") { ... } else { ... }` opens with `let _rnd_X
        // = macros1.either(...)` immediately followed by a bold text() call whose template embeds
        // `{_rnd_X}` (ConsolidateTextNodes merges the inline either() + surrounding text() calls
        // into one TextNode carrying `Lets: ["_rnd_X"]`). This used to be rejected by a Lets-
        // emptiness guard on the theory that hoisting would strand the `let` — but the `let` node
        // itself is never removed (only the TextNode that consumed it is; the `let` stays in
        // headingPrefix, still executing normally as part of the body). Confirmed against the
        // engine: title evaluates strictly AFTER the full body renders (PassageRenderer.Render), a
        // `let`'s value persists in the same VariableStore instance title expansion reads from
        // (ClearLetScope has no call sites in engine code), and a `{letname}` placeholder embedded
        // in one ternary arm's own string literal gets its own recursive ExpandTemplate pass once
        // that arm is selected — so this hoists correctly and safely.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.huntresult == "success")
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A ");
                        yield return base.text(this.macros1.either(new StoryVar[] { "Righteous", "Wondrous" }));
                        yield return base.text(" Fate");
                    }
                    yield return base.lineBreak();
                    yield return base.text("Body text A.");
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Overrun By ");
                        yield return base.text(this.macros1.either(new StoryVar[] { "Demons", "Monsters" }));
                    }
                    yield return base.lineBreak();
                    yield return base.text("Body text B.");
                }
                yield break;
            }
            """);

        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        // The let nodes must survive in the body (never removed) — only the bold TextNode that
        // consumed each one is gone, hoisted into the title.
        Assert.All(cond.Branches, b => Assert.Contains(b.Nodes, n => n is LetNode));
        Assert.All(cond.Branches, b => Assert.DoesNotContain(b.Nodes, n => n is TextNode { Style: "bold" }));

        var letVarA = ((LetNode)cond.Branches[0].Nodes.OfType<LetNode>().First()).Var;
        var letVarB = ((LetNode)cond.Branches[1].Nodes.OfType<LetNode>().First()).Var;
        Assert.Equal(
            $"{{huntresult == \"success\" ? \"A {{{letVarA}}} Fate\" : \"Overrun By {{{letVarB}}}\"}}",
            passages[0].Title);
        Assert.Null(passages[0].Subtitle);
    }

    [Fact]
    public void NarrationLayout_TopLevelHeadingDependsOnTwoPrecedingLets_StillHoists()
    {
        // Regression: Fear of the Unknown's AsylumTreatment — TWO top-level `let`s (no conditional
        // branching at all, unlike the previous test) immediately precede a bold heading whose
        // template embeds both: `let _rnd_0 = rand_between(...); let _rnd_1 = [...].shuffled(...)[0];
        // **Asylum Admittance Log {_rnd_0}{_rnd_1}**`. Both lets sit in headingPrefix (skipped by the
        // prefix-skip, not consumed) and remain in the body exactly where they were, executing
        // normally before title evaluation — same reasoning as the previous test, just with two
        // lets instead of one and no surrounding conditional.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Asylum Admittance Log ");
                    yield return base.text(this.macros1.either(new StoryVar[] { 151, 152, 153 }));
                    yield return base.text(this.macros1.either(new StoryVar[] { "A", "B", "C" }));
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        var letNodes = passages[0].Nodes.OfType<LetNode>().ToList();
        Assert.Equal(2, letNodes.Count);
        Assert.DoesNotContain(passages[0].Nodes, n => n is TextNode { Style: "bold" });
        Assert.Equal(
            $"Asylum Admittance Log {{{letNodes[0].Var}}}{{{letNodes[1].Var}}}",
            passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_LongLetNamesInflateRawTemplateOverCutoff_StillHoistsViaEstimatedLength()
    {
        // Regression: the previous test's auto-generated let names (_rnd_P1_0/_rnd_P1_1) are short
        // enough that "Asylum Admittance Log {_rnd_P1_0}{_rnd_P1_1}" stays under 50 characters even
        // counting the placeholders literally — it never actually exercised the length ESTIMATION
        // logic. The real AsylumTreatment passage's auto-generated names embed the full passage
        // name (_rnd_AsylumTreatment_0/_rnd_AsylumTreatment_1, 24 chars each), pushing the RAW
        // template to 73 characters — over the 50-char cutoff — even though the actual rendered
        // text at play time (a short number + a single letter) is nowhere near that long.
        // EstimateRenderedLength treats each placeholder as a small fixed-length stand-in rather
        // than its own literal character count, so this must still hoist. Using a long passage name
        // here specifically to reproduce that same long-auto-generated-name effect.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["AVeryLongPassageNameForTesting"] = new StoryPassage("AVeryLongPassageNameForTesting", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Asylum Admittance Log ");
                    yield return base.text(this.macros1.either(new StoryVar[] { 151, 152, 153 }));
                    yield return base.text(this.macros1.either(new StoryVar[] { "A", "B", "C" }));
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        var letNodes = passages[0].Nodes.OfType<LetNode>().ToList();
        Assert.Equal(2, letNodes.Count);
        var rawTemplate = $"Asylum Admittance Log {{{letNodes[0].Var}}}{{{letNodes[1].Var}}}";
        Assert.True(rawTemplate.Length > 50, $"test fixture should reproduce the raw-length-over-cutoff scenario (was {rawTemplate.Length} chars)");
        Assert.DoesNotContain(passages[0].Nodes, n => n is TextNode { Style: "bold" });
        Assert.Equal(rawTemplate, passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_SwitchWithTrailingClickThroughLink_StillCollapsesToTernaryTitle()
    {
        // Regression: A Time of War's SeedResolution and Fear of the Unknown's PEWitch2 — a
        // heading-bearing switch/conditional followed by a REAL, visible "Click to continue..."
        // link (ExpandLinkNode, the hook+enchantHook idiom, not an auto-show SetupBlockNode — those
        // are a DIFFERENT source pattern, `styleScope("setupStyle")`, already covered by an earlier
        // test). A click-through link's own label is never itself a plausible heading — same
        // reasoning already applied to breaks/assigns — so it shouldn't block
        // TryFindSoleHeadingCandidate<SwitchNode> from finding the switch as the sole candidate.
        // Before ExpandLinkNode joined IsHeadingInert, this link was treated as a second,
        // non-matching "candidate" and the whole hoist was rejected.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.gunsbonus == 1)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Place of Knowledge");
                    }
                }
                else if (this.Vars.gunsbonus == 2)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Wealth of Goods");
                    }
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("An Investor's Paradise");
                    }
                }
                yield return base.lineBreak();
                using (base.styleScope("hook", "h0000024"))
                    yield return base.link("Click to continue...", null, () => base.enchantHook("h0000024", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                yield return base.text("The player with the Storybook must choose one option.");
                yield break;
            }
            """);

        Assert.Equal(
            "{gunsbonus == 1 ? \"A Place of Knowledge\" : gunsbonus == 2 ? \"A Wealth of Goods\" : \"An Investor's Paradise\"}",
            passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_LeadingBoldSpanMergedWithShortPlainRemainder_HoistsJustTheSpan()
    {
        // Regression: Fear of the Unknown's FPYesHub — `styleScope("bold"){ text("Destiny Awaits") }
        // text("Your choice has been made.")`, no lineBreak between the closing style scope and the
        // next text() call. CanJoinGroup merges adjacent TextNodes regardless of style match
        // whenever nothing separates them, producing ONE TextNode with Style: null (mixed) and
        // Template: "**Destiny Awaits**Your choice has been made." — the flat shape-1 check
        // (requiring Style == "bold" on the WHOLE node) never matched this at all. The combined
        // length here (14 + 27 = 41 chars) is short, so this hoists — contrast with the sibling test
        // for OptiontoKillYesPattern's shape, where the remainder is long enough to correctly block
        // the hoist instead.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Destiny Awaits");
                }
                yield return base.text("Your choice has been made.");
                yield return base.lineBreak();
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Immediately Gain 1VP.");
                }
                yield break;
            }
            """);

        Assert.Equal("Destiny Awaits", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        var body = Assert.Single(passages[0].Nodes.OfType<TextNode>(), t => t.Template == "Your choice has been made.");
        Assert.Null(body.Style);
    }

    [Fact]
    public void NarrationLayout_LeadingConditionalFragmentThenBoldContinuation_CombinesIntoOneTitle()
    {
        // Regression: Fear of the Unknown's Player1Statsfin — `if (warriorA) { let _rpl =
        // warriorA.replace("_1", ""); **{_rpl}** }` (single branch, no else) immediately followed
        // (no break) by a second, unconditional bold TextNode (`** Complete**`). Neither the flat
        // shape-1/2 checks (headingSuffix[0] here is a ConditionalNode, not a TextNode) nor
        // TryFindSoleHeadingCandidate (the trailing bold TextNode is a second, non-matching
        // "candidate") could reach this on their own — the conditional's own recursive hoist
        // ("{_rpl}", a let-derived fragment) has to be combined with the immediately-following
        // static bold text to form the complete heading.
        //
        // The combined text is wrapped in a ternary matching the SAME guard condition
        // (`warriorA`) the body itself uses, not flatly concatenated — the fragment's own `let`
        // only actually runs when that condition is true (e.g. before the player's first name
        // submission, `warriorA` is unset and this branch never executes at all), so an
        // unconditional title would reference a `let` that was never bound on that first visit.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.warriorA)
                {
                    this.Vars.dummy = 1;
                }
                if (this.Vars.warriorA)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text(this.Vars.warriorA.Replace("_1", ""));
                    }
                }
                using (base.styleScope("bold", true))
                {
                    yield return base.text(" Complete");
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        var letNode = passages[0].Nodes.OfType<ConditionalNode>().ElementAt(1).Branches[0].Nodes.OfType<LetNode>().Single();
        Assert.Equal($"{{warriorA ? \"{{{letNode.Var}}} Complete\" : \"Complete\"}}", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.DoesNotContain(passages[0].Nodes, n => n is TextNode { Style: "bold" });
    }

    [Fact]
    public void NarrationLayout_BoldHeadingOverMaxLength_IsNotHoisted()
    {
        // Regression: A Time of War's Amessyes/AMessenger2/Militarytoken/DuelResolution1, and Fear
        // of the Unknown's AsylumQuestion5/7/9 — a physical-component handling instruction to the
        // player ("Carefully hand this Storybook device to the player with the {crest} token and do
        // not allow any other players to see the screen.") or a narrative question prompt is ALSO
        // authored as a leading `styleScope("bold", true)` block, structurally identical to a real
        // title/heading — Cradle has no separate macro/tag distinguishing "this bold text is a
        // title" from "this bold text is emphasized body content"; both use the exact same API.
        // Confirmed against the decompiled original Unity app
        // (Assets/Cradle/Players/TwineTMProPlayer/Script/TwineTMProPlayer.cs, RefreshText()): a
        // leading bold run IS provisionally treated as a title candidate purely by position, but
        // then demoted back into ordinary body text (title UI cleared) if the accumulated text
        // exceeds exactly 50 characters. MaxHeadingLength reproduces that same cutoff — not a
        // guessed threshold, the actual one the reference app uses.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Carefully hand this Storybook device to the player with the ");
                    yield return base.text(this.Vars.crest);
                    yield return base.text(" token and do not allow any other players to see the screen.");
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        Assert.Equal("P1", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.Contains(passages[0].Nodes, n => n is TextNode { Style: "bold" });
    }

    [Fact]
    public void IntroductionLayout_TwoBoldLinesCombinedOverMaxLength_FallsBackToShape1TitleOnly()
    {
        // The MaxHeadingLength cutoff applies to shape 2's COMBINED title+subtitle length (mirroring
        // the single accumulated titleString the original app builds from both lines together), not
        // each line independently — here the first line alone is short enough to pass on its own,
        // but adding the second pushes the combined text over the limit, so shape 2 is rejected and
        // this falls back to shape 1 (first line only, second line left as ordinary body text) rather
        // than being rejected outright.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("The Long-Awaited Announcement");
                    yield return base.lineBreak();
                    yield return base.text("Of the Grand Ducal Council to All Assembled Citizens");
                }
                StyleScope styleScope = null;
                yield return base.lineBreak();
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        Assert.Equal("The Long-Awaited Announcement", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.Contains(passages[0].Nodes, n => n is TextNode { Style: "bold", Template: "Of the Grand Ducal Council to All Assembled Citizens" });
    }

    [Fact]
    public void NarrationLayout_BoldLeadingTextFollowedByBoldSwitchNoBreak_IsNotHoisted()
    {
        // Regression: A Time of War's TownHallS1 — `text("Carefully hand this Storybook device to
        // Player "); Vars.th++; if (th==1) text(nameA); else if (th==2) text(nameB); ...`, all
        // still inside ONE open styleScope("bold", true) with no lineBreak between the leading text
        // and the if/elseif chain (consolidated to a SwitchNode). Confirmed against the decompiled
        // original Unity app: title-text accumulation continues across EVERY StoryText output while
        // the bold style-group stays open, regardless of source-level branching — so the real title
        // is "Carefully hand this Storybook device to Player " + whichever name wins, which is
        // ALWAYS well over 50 chars even though the static leading fragment alone (49 chars) looks
        // short enough on its own. Checking first.Template's length in isolation was wrong; this
        // must bail out entirely rather than hoist a truncated title.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Carefully hand this Storybook device to Player ");
                    this.Vars.th = this.Vars.th + 1;
                    if (this.Vars.th == 1)
                    {
                        yield return base.text(this.Vars.nameA);
                    }
                    else if (this.Vars.th == 2)
                    {
                        yield return base.text(this.Vars.nameB);
                    }
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        Assert.Equal("P1", passages[0].Title); // unhoisted: MwsPassage.Title falls back to the passage name
        Assert.Null(passages[0].Subtitle);
        Assert.Contains(passages[0].Nodes, n => n is TextNode { Style: "bold" });
    }

    [Fact]
    public void NarrationLayout_BoldLeadingTextFollowedByShortStaticBoldSuffix_StillHoistsLeadingTextOnly()
    {
        // Regression: Cost of Disease's DetEffectRandom — "The Effects of Immortality " is followed
        // (no break, still inside the same open styleScope) by an if-chain choosing between two
        // SHORT, STATIC bold suffixes ("- Early Years"/"- Middle Years") or neither, depending on
        // `round`. Unlike TownHallS1 (previous test), the continuation here is bounded: every
        // reachable branch is a literal string, not a `{var}` placeholder, so the worst-case combined
        // length ("The Effects of Immortality - Middle Years", well under 50) can be computed
        // statically and is safely short — MaxPossibleBoldContinuationLength must return a real
        // bound here (not null/unbounded) so this doesn't get wrongly disqualified the same way
        // TownHallS1 correctly is. The hoisted title is still just the leading text alone (shape 1
        // never tries to splice the conditional suffix in) — that's pre-existing, accepted behavior,
        // not something this test is about; the point is only that it doesn't get REMOVED.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("The Effects of Immortality ");
                    if (this.Vars.round == 16)
                    {
                        yield return base.text("- Early Years");
                    }
                    if (this.Vars.round == 17)
                    {
                        yield return base.text("- Middle Years");
                    }
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        Assert.Equal("The Effects of Immortality", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
    }

    [Fact]
    public void NarrationLayout_SeparateGuardConditionalsEachWithOwnHeading_CollapsesToTernaryTitleWithBlankFallback()
    {
        // Regression: A Time of War's ParadoxEvent (source lines 10010-10052) — two SEPARATE
        // top-level `if (cond) { ... }` conditionals, neither with an `else`, each guarding a
        // distinct value of `timemistake` and each carrying its own bold heading. Not an
        // if/elseif/else chain (BuildTernaryArmsFromConditional's own case, which requires a real
        // `else` branch to anchor on) or a switch (BuildTernaryArmsFromSwitch's, which requires a
        // `default` case) — two independent ConditionalNode siblings with no catch-all branch at
        // all. TryFindSoleHeadingCandidate<ConditionalNode> also can't handle this: it bails as soon
        // as it sees a SECOND non-inert ConditionalNode. Collapses to a ternary title with a
        // synthetic "" fallback for the case neither guard matches — the conditions look complete/
        // non-overlapping by construction (distinct values of the same variable) but that isn't
        // something this can prove statically, so an empty fallback is the safe default.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.timemistake < 8)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Monument to Progress");
                    }
                    yield return base.lineBreak();
                    yield return base.text("Body text A.");
                }
                if (this.Vars.timemistake == 8)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Rebuilding");
                    }
                    yield return base.lineBreak();
                    yield return base.text("Body text B.");
                }
                yield break;
            }
            """);

        Assert.Equal(
            "{timemistake < 8 ? \"Monument to Progress\" : timemistake == 8 ? \"Rebuilding\" : \"\"}",
            passages[0].Title);
        Assert.Null(passages[0].Subtitle);

        var conds = passages[0].Nodes.OfType<ConditionalNode>().ToList();
        Assert.Equal(2, conds.Count);
        Assert.All(conds, c => Assert.DoesNotContain(c.Branches[0].Nodes, n => n is TextNode { Style: "bold" }));
    }

    [Fact]
    public void NarrationLayout_GuardChainWithNestedTernaryHeadingPerArm_SplicesRatherThanQuotesLiterally()
    {
        // Regression: Cost of Disease's AllMWRewards — the guard-chain case above (two/five separate
        // top-level `if` guards, no else), but here EACH guard's own branch doesn't carry a flat bold
        // heading directly; it carries its OWN if/elseif/else chain (consolidated to a SwitchNode)
        // whose branches each have a heading, so each guard's own TryHoistHeadingTitleSubtitle call
        // recurses into the EXISTING if/elseif/else ternary path and comes back with an ALREADY
        // "{...}"-wrapped title, not a literal string. Before AsTernaryArm, TryBuildGuardChainHeading
        // (and TryBuildTernaryHeading, which has the identical latent bug) fed that hoisted title
        // straight to BuildTernaryChain, which quotes it as a literal string containing literal
        // "{"/'"'/"}" characters — the outer title ends up holding the INNER ternary's un-evaluated
        // source text instead of splicing it in. AsTernaryArm re-wraps an already-braced hoisted title
        // as "${...}" so BuildTernaryChain's own pre-existing ${...}-unwrap rule (built for target/
        // goto arms) splices it for free.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.tempcomp == this.Vars.nameA)
                {
                    if (this.Vars.typeA == 1)
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Alpha One");
                        }
                    }
                    else
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Alpha Two");
                        }
                    }
                }
                if (this.Vars.tempcomp == this.Vars.nameB)
                {
                    if (this.Vars.typeB == 1)
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Beta One");
                        }
                    }
                    else
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Beta Two");
                        }
                    }
                }
                yield break;
            }
            """);

        Assert.NotNull(passages[0].Title);
        Assert.DoesNotContain('{', passages[0].Title![1..^1]); // no un-spliced nested "{...}" left inside the outer braces
        Assert.Equal(
            "{tempcomp == nameA ? typeA == 1 ? \"Alpha One\" : \"Alpha Two\" : tempcomp == nameB ? typeB == 1 ? \"Beta One\" : \"Beta Two\" : \"\"}",
            passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_MultipleOuterBranchesAgreeOnSameHeading_HoistsSharedTitle()
    {
        // Regression: A Time of War's BarracksSimple1/2/3 — two outer branches (bldg1=="X"/!bldg1)
        // each wrap their OWN inner conditional whose `then` starts with the IDENTICAL bold "Service
        // Required" heading and whose `else` is a bare goto; a third outer branch (the catch-all
        // else) is also just a goto. TryHoistFromOneBranch's per-branch probing finds BOTH real outer
        // branches independently hoist "Service Required" (via the inner conditional's own
        // sole-candidate hoist one level down) — since they agree on the exact same title, that's not
        // ambiguous (the player sees the same heading no matter which branch fires), so the shared
        // title is hoisted from BOTH branches at once rather than left untouched. Before this fix
        // (which also predates the multi-agreement extension), a per-branch probing call mutated the
        // inner conditionals in place as a side effect of merely being tried, so even a REJECTED
        // ambiguous hoist had already silently deleted the heading text; WithBranchesNodes' own
        // clone-and-substitute non-mutation is what makes it safe to commit BOTH branches' removals
        // here without corrupting anything else in the tree.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.bldg1 == "X")
                {
                    if (this.Vars.warwinner == "Y")
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Service Required");
                        }
                        yield return base.lineBreak();
                        yield return base.text("Body A.");
                    }
                    else
                    {
                        yield return base.abort(goToPassage: "Elsewhere");
                    }
                }
                else if (!this.Vars.bldg1)
                {
                    if (this.Vars.warwinner == "Y")
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Service Required");
                        }
                        yield return base.lineBreak();
                        yield return base.text("Body B.");
                    }
                    else
                    {
                        yield return base.abort(goToPassage: "Elsewhere");
                    }
                }
                else
                {
                    yield return base.abort(goToPassage: "Elsewhere");
                }
                yield break;
            }
            """);

        Assert.Equal("Service Required", passages[0].Title);
        var boldNodes = passages[0].Nodes.OfType<ConditionalNode>()
            .SelectMany(c => c.Branches)
            .SelectMany(b => b.Nodes)
            .OfType<ConditionalNode>()
            .SelectMany(c => c.Branches)
            .SelectMany(b => b.Nodes)
            .Where(n => n is TextNode { Style: "bold" })
            .ToList();
        Assert.Empty(boldNodes); // hoisted out of both inner conditionals, not duplicated or left behind
        var bodyTexts = passages[0].Nodes.OfType<ConditionalNode>()
            .SelectMany(c => c.Branches)
            .SelectMany(b => b.Nodes)
            .OfType<ConditionalNode>()
            .SelectMany(c => c.Branches)
            .SelectMany(b => b.Nodes)
            .OfType<TextNode>()
            .Select(t => t.Template)
            .ToList();
        Assert.Contains("Body A.", bodyTexts);
        Assert.Contains("Body B.", bodyTexts);
    }

    [Fact]
    public void NarrationLayout_MultipleOuterBranchesDisagreeOnHeading_LeavesHeadingTextIntact()
    {
        // Contrast with the "agree" case above: when the outer branches' own headings DIFFER
        // ("Service Required" vs. "Optional Duty"), there's no single unambiguous title to hoist —
        // this is the genuinely-ambiguous case TryHoistFromOneBranch still correctly refuses (only
        // exact (title, subtitle) agreement across every succeeding branch counts). Pins the original
        // mutation-safety regression this shape was written for: a rejected hoist must never have
        // already deleted the heading text from the branches it recursively probed.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.bldg1 == "X")
                {
                    if (this.Vars.warwinner == "Y")
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Service Required");
                        }
                        yield return base.lineBreak();
                        yield return base.text("Body A.");
                    }
                    else
                    {
                        yield return base.abort(goToPassage: "Elsewhere");
                    }
                }
                else if (!this.Vars.bldg1)
                {
                    if (this.Vars.warwinner == "Y")
                    {
                        using (base.styleScope("bold", true))
                        {
                            yield return base.text("Optional Duty");
                        }
                        yield return base.lineBreak();
                        yield return base.text("Body B.");
                    }
                    else
                    {
                        yield return base.abort(goToPassage: "Elsewhere");
                    }
                }
                else
                {
                    yield return base.abort(goToPassage: "Elsewhere");
                }
                yield break;
            }
            """);

        Assert.Equal("P1", passages[0].Title); // genuinely ambiguous: no hoist, but that's fine
        var boldNodes = passages[0].Nodes.OfType<ConditionalNode>()
            .SelectMany(c => c.Branches)
            .SelectMany(b => b.Nodes)
            .OfType<ConditionalNode>()
            .SelectMany(c => c.Branches)
            .SelectMany(b => b.Nodes)
            .Where(n => n is TextNode { Style: "bold" })
            .Select(n => ((TextNode)n).Template)
            .ToList();
        Assert.Equal(["Service Required", "Optional Duty"], boldNodes); // both must survive, neither hoisted
    }

    [Fact]
    public void NarrationLayout_LeadingSetupPopup_StillHoistsTitle()
    {
        // Regression: A Time of War's 2pFamineBidRes — an auto-show `setupStyle` popup
        // (SetupBlockNode) sits before the passage's own real content, with no break in between at
        // the top level. SetupBlockNode renders as a separate overlay, not a position in the
        // passage's own document flow — same reasoning already applied to
        // EndOfGenerationNode/InputPromptNode — so it must be skipped over by the prefix-skip to
        // find "Bid Outcome" behind it, exactly as those two already are.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("setupStyle", true))
                {
                    yield return base.text("The player who bid the most coins pays them to supply and gains 4VP.");
                }
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Bid Outcome");
                }
                yield return base.lineBreak();
                yield return base.text("Body text.");
                yield break;
            }
            """);

        Assert.Equal("Bid Outcome", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.DoesNotContain(passages[0].Nodes, n => n is TextNode { Style: "bold" });
    }

    [Fact]
    public void NarrationLayout_SwitchWithTrailingSetupPopup_StillCollapsesToTernaryTitle()
    {
        // Regression: A Time of War's SeedResolution — `switch (gunsbonus) { case 1: **A Place of
        // Knowledge** ...; case 2: **A Wealth of Goods** ...; default: **An Investor's Paradise**
        // ...; }` followed by a break, then a trailing auto-show setupStyle popup
        // (SetupBlockNode) — before SetupBlockNode joined IsHeadingInert,
        // TryFindSoleHeadingCandidate<SwitchNode> saw the popup as a second, non-matching
        // "candidate" and bailed entirely, even though the switch's own 3-way ternary title is
        // otherwise straightforward (has a default, matching BuildTernaryArmsFromSwitch's own
        // requirement).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.gunsbonus == 1)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Place of Knowledge");
                    }
                }
                else if (this.Vars.gunsbonus == 2)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Wealth of Goods");
                    }
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("An Investor's Paradise");
                    }
                }
                yield return base.lineBreak();
                using (base.styleScope("setupStyle", true))
                {
                    yield return base.text("The player with the Storybook must choose one option.");
                }
                yield break;
            }
            """);

        Assert.Equal(
            "{gunsbonus == 1 ? \"A Place of Knowledge\" : gunsbonus == 2 ? \"A Wealth of Goods\" : \"An Investor's Paradise\"}",
            passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_IfElseBothWithHeadingsThenTrailingSetupPopup_StillCollapsesToTernaryTitle()
    {
        // Regression: Fear of the Unknown's PEWitch2 — `if (path4 == "PECreature") { **Progression**
        // ... } else { **Regression** ... }` followed by a trailing auto-show setupStyle popup — the
        // condition is exhaustive and BOTH branches carry their own heading, so this should collapse
        // to a straightforward two-way ternary title once the popup no longer blocks
        // TryFindSoleHeadingCandidate<ConditionalNode> from finding the conditional as the sole
        // candidate.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.path4 == "PECreature")
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Progression");
                    }
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Regression");
                    }
                }
                using (base.styleScope("setupStyle", true))
                {
                    yield return base.text("Retrieve the Obsession card.");
                }
                yield break;
            }
            """);

        Assert.Equal("{path4 == \"PECreature\" ? \"Progression\" : \"Regression\"}", passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_ChainOfGotoGuardsThenRealContent_StillHoistsTitle()
    {
        // Regression: Fear of the Unknown's AsylumMeet — five separate top-level `if (Xv > 1) { goto
        // AsylumComplete; }` guards (no else), the last of which ALSO has an else branch containing
        // the passage's real content and its own bold heading "Retrieval of Property". GotoNode
        // never produces output — same reasoning as EffectNode/LetNode — and a render that fires one
        // is never shown to the player (GameSession.RenderChainFrom discards the whole intermediate
        // PassageRenderResult, title included), so a `then: [goto]`-only branch/conditional is safe
        // to treat as heading-inert. Before GotoNode joined IsHeadingInert, the first four
        // goto-only conditionals were NOT classified as entirely inert (their own then-branch content
        // — a bare GotoNode — failed IsHeadingInert), so TryFindSoleHeadingCandidate saw multiple
        // non-inert ConditionalNode "candidates" and bailed before ever reaching the fifth.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.Av > 1)
                {
                    yield return base.abort(goToPassage: "AsylumComplete");
                }
                if (this.Vars.Bv > 1)
                {
                    yield return base.abort(goToPassage: "AsylumComplete");
                }
                if (this.Vars.Cv > 1)
                {
                    yield return base.abort(goToPassage: "AsylumComplete");
                }
                if (this.Vars.Dv > 1)
                {
                    yield return base.abort(goToPassage: "AsylumComplete");
                }
                if (this.Vars.Ev > 1)
                {
                    yield return base.abort(goToPassage: "AsylumComplete");
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Retrieval of Property");
                    }
                    yield return base.lineBreak();
                    yield return base.text("Body text.");
                }
                yield break;
            }
            """);

        Assert.Equal("Retrieval of Property", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
    }

    [Fact]
    public void NarrationLayout_LeadingIfElseTernaryThenSharedTrailingContent_StillHoistsTitle()
    {
        // Regression: A Time of War's MonuRes/BenevolenceBonus — `if (cond) { **Heading A** ... }
        // else { **Heading B** ... }` immediately followed (same top level, no wrapping conditional)
        // by more UNCONDITIONAL body text shared by both outcomes. The old ConditionalNode
        // "sole candidate" check required the WHOLE top-level node list to be nothing but the
        // conditional plus heading-inert siblings — any real trailing content (here, a plain
        // `text()` after the conditional) disqualified the hoist entirely, even though the trailing
        // text has nothing to do with which heading fired. The conditional HAS an else
        // (self-contained, both outcomes already covered), so it's safe to hoist regardless of what
        // real content follows.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.release >= 1)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Bend Towards Chaos");
                    }
                    yield return base.lineBreak();
                    yield return base.text("Chaos body.");
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Secure Future");
                    }
                    yield return base.lineBreak();
                    yield return base.text("Secure body.");
                }
                yield return base.lineBreak();
                yield return base.text("Because of our decision, shared trailing text.");
                yield break;
            }
            """);

        Assert.Equal(
            "{release >= 1 ? \"A Bend Towards Chaos\" : \"A Secure Future\"}",
            passages[0].Title);
        Assert.Null(passages[0].Subtitle);
        Assert.Contains(passages[0].Nodes, n => n is TextNode t && t.Template == "Because of our decision, shared trailing text.");
        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        Assert.All(cond.Branches, b => Assert.DoesNotContain(b.Nodes, n => n is TextNode { Style: "bold" }));
    }

    [Fact]
    public void NarrationLayout_LeadingSwitchWithoutDefaultThenSharedTrailingContent_StillHoistsTernaryTitle()
    {
        // Regression: A Time of War's ParadoxTimeRandom — `switch (timemistake) { case 1: **A Surge
        // of Productivity** ...; case 2: **A Clarity of Purpose** ...; ... }` (8 cases in the real
        // passage, no `default:` — `timemistake` is always 1-8 by construction) immediately followed
        // by more unconditional trailing content (a setup popup in the real passage; a plain
        // paragraph + text is enough to exercise the same shape). BuildTernaryArmsFromSwitch now
        // builds arms straight from the declared cases regardless of whether a default exists, and
        // TryBuildTernaryHeading appends a synthetic "" fallback arm when there wasn't one —
        // mirroring the guard chain's own "declared cases exhaust the value space, but that isn't
        // provable statically" trade.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.timemistake == 1)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Surge of Productivity");
                    }
                }
                if (this.Vars.timemistake == 2)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Clarity of Purpose");
                    }
                }
                yield return base.lineBreak();
                yield return base.text("Shared trailing text.");
                yield break;
            }
            """);

        Assert.Equal(
            "{timemistake == 1 ? \"A Surge of Productivity\" : timemistake == 2 ? \"A Clarity of Purpose\" : \"\"}",
            passages[0].Title);
        Assert.Contains(passages[0].Nodes, n => n is TextNode t && t.Template == "Shared trailing text.");
    }

    [Fact]
    public void NarrationLayout_SoleSwitchWithoutDefault_StillHoistsTernaryTitle()
    {
        // Regression: Cost of Disease's Diseases1 — `switch (disease1) { case 1: **A Year of
        // Sickness** ...; case 2: **Rest and Time** ...; }`, no default (disease1 is always 1 or 2
        // via rand_between(1, 2, ...)). Even with NOTHING else at the top level competing for the
        // heading position, the missing default previously blocked BuildTernaryArmsFromSwitch
        // outright, so TryBuildTernaryHeading was never even attempted.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.disease1 == 1)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("A Year of Sickness");
                    }
                }
                if (this.Vars.disease1 == 2)
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("Rest and Time");
                    }
                }
                yield break;
            }
            """);

        Assert.Equal(
            "{disease1 == 1 ? \"A Year of Sickness\" : disease1 == 2 ? \"Rest and Time\" : \"\"}",
            passages[0].Title);
    }

    [Fact]
    public void NarrationLayout_LeadingDynamicIncludePassageThenHeading_StillHoistsTitle()
    {
        // Regression: Cost of Disease's HuntNight1/HuntNight2 — `base.passage(this.Vars.direction,
        // ...)` (a dynamic ${direction} include) immediately followed by a bold heading built from a
        // session variable (`**{huntreward1}.**`). IncludePassageNode wasn't heading-inert, so it was
        // treated as a competing, non-candidate top-level node and the hoist bailed before ever
        // looking past it.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.passage(this.Vars.direction, System.Array.Empty<StoryVar>());
                yield return base.lineBreak();
                using (base.styleScope("bold", true))
                {
                    yield return base.text(this.Vars.huntreward1);
                    yield return base.text(".");
                }
                yield break;
            }
            """);

        Assert.Equal("{huntreward1}.", passages[0].Title);
        Assert.Contains(passages[0].Nodes, n => n is IncludePassageNode);
    }

    [Fact]
    public void NarrationLayout_LeadingConditionalThenTrailingWhitespaceOnlyText_StillHoistsTitle()
    {
        // Regression: A Time of War's Stickfun — `if (crazy == 0) { abort(goToPassage: "CoWEvent");
        // } else { **The Formation** ... }` followed, at the very end of the passage, by a trailing
        // `lineBreak(); text(" ");` (a deliberate blank-line spacer, a real Cradle authoring idiom,
        // not an extraction artifact). A whitespace-only TextNode isn't heading-inert, so it used to
        // compete as a second non-inert top-level node and block the hoist entirely — now it's
        // simply passed through as ordinary trailing content, same as any other real content after a
        // self-contained if/else.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.crazy == 0)
                {
                    yield return base.abort(goToPassage: "CoWEvent");
                }
                else
                {
                    using (base.styleScope("bold", true))
                    {
                        yield return base.text("The Formation");
                    }
                    yield return base.lineBreak();
                    yield return base.text("Body text.");
                }
                yield return base.lineBreak();
                yield return base.text(" ");
                yield break;
            }
            """);

        Assert.Equal("The Formation", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
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

        // Swapped: the reference app shows "GENERATION I" as the small subtitle beneath the
        // descriptive title, not the other way around, even though it's first in the source text
        // — see SwapIfGenerationLabel.
        Assert.Equal("Yellow Fever", passages[0].Title);
        Assert.Equal("GENERATION I", passages[0].Subtitle);
        var body = passages[0].Nodes.OfType<TextNode>().Single();
        Assert.Equal("The siblings' arrival to claim their considerable inheritance...", body.Template);
    }

    [Fact]
    public void IntroductionLayout_GenerationDashSplit_SwapsTitleAndSubtitle()
    {
        // Real-world source: A Time of War's PeaceIntro ("GENERATION III - Peace Through War").
        // The plain dash-split already worked (title="GENERATION III", subtitle="Peace Through
        // War"), just in the wrong order — same swap as the two-line shape above.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "INTRO" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("GENERATION III - Peace Through War");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        Assert.Equal("Peace Through War", passages[0].Title);
        Assert.Equal("GENERATION III", passages[0].Subtitle);
    }

    [Fact]
    public void IntroductionLayout_GenerationColonSplit_SplitsAndSwaps()
    {
        // Real-world source: Fear of the Unknown's FearoftheUnknownStart
        // ("GENERATION I: Fear of the Unknown") and A Time of War's ATimeofWarIntro
        // ("GENERATION I: Taking Sides") — a single bold line with no " - " dash at all, so the
        // plain dash-split never fired and the whole line became an unsplit title. Colon-splitting
        // is scoped specifically to this "GENERATION {roman}:" shape (see SplitHeadingLine's
        // remarks) — a general "any colon splits" rule isn't safe (see the next test).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "INTRO" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("GENERATION I: Fear of the Unknown");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        Assert.Equal("Fear of the Unknown", passages[0].Title);
        Assert.Equal("GENERATION I", passages[0].Subtitle);
    }

    [Fact]
    public void IntroductionLayout_NonGenerationColonHeading_StaysUnsplit()
    {
        // Real-world source: A Time of War's ForewordScen2
        // ("A Time of War : A Memoir Across Three Generations") — a single bold line with a colon
        // that ISN'T the "GENERATION {roman}:" shape. Colon-splitting must not fire here.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { "INTRO" }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("A Time of War : A Memoir Across Three Generations");
                }
                StyleScope styleScope = null;
                yield break;
            }
            """);

        Assert.Equal("A Time of War : A Memoir Across Three Generations", passages[0].Title);
        Assert.Null(passages[0].Subtitle);
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
        // Regression: Cost of Disease's MostInvestigated passage. The old output —
        // max(scores) / countif(=max_scores, scores) — is invalid MWS on both counts: max(...) is
        // a function taking individual scalar args, not an array (no array-taking max exists at
        // all — "Cannot convert array to int" once `scores`, a genuine array per the LetNode.Array
        // temporary-array mechanism, reached AsInt()); and countif is a METHOD on the array
        // (arr.countif(pattern)), not a bare function, with an unquoted bare-text pattern the
        // engine has no way to interpolate. Correct: max(<the scalars scores was built from>), and
        // scores.countif("=" + max_scores) (a real quoted, concatenated pattern expression).
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
        Assert.Equal("max(scoreA, scoreB)", lets[1].Compute);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().First();
        Assert.Equal("scores.countif(\"=\" + max_scores) > 1", cond.Branches[0].Condition);
    }

    [Fact]
    public void LinqCountIfMax_MethodSyntax_EmitsMaxLetAndSubstitutesCondition()
    {
        // Same idiom as LinqCountIfMax_EmitsMaxLetAndSubstitutesCondition, but the method-syntax
        // spelling (arr.Where(x => x == arr.Max()).Count()) instead of query syntax — real
        // occurrence: MostInvestigated's actual source uses this form, not the query-syntax one.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                List<int> play = new List<int>(new int[] { this.Vars.playA, this.Vars.playB, this.Vars.playC, this.Vars.playD, this.Vars.playE });
                int numberOfMax = play.Where(value => value == play.Max()).Count();
                if (numberOfMax > 1) { this.Vars.most = 0; }
                yield break;
            }
            """);

        var lets = passages[0].Nodes.OfType<LetNode>().ToList();
        Assert.Equal(2, lets.Count);
        Assert.Equal("play", lets[0].Var);
        Assert.Equal("max_play", lets[1].Var);
        Assert.Equal("max(playA, playB, playC, playD, playE)", lets[1].Compute);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().First();
        Assert.Equal("play.countif(\"=\" + max_play) > 1", cond.Branches[0].Condition);
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

    [Fact]
    public void Passage_TargetedByIncludePassage_DoesNotHoistTitle()
    {
        // Regression: Fear of the Unknown's letter1a/journal1a/date1a — each starts with a bold
        // heading-shaped leading TextNode, so title-hoisting normally moves that node into `title:`
        // and removes it from `nodes:`. But these three passages are never displayed on their own —
        // every reference to them is a static base.passage("letter1a", ...) inclusion from another
        // passage, which splices their Nodes verbatim into the includer's own body at render time
        // (see IncludePassageNode handling in V2Serializer/PassageRenderer). An included passage's
        // own `title` is never independently rendered, so hoisting one out would silently delete a
        // content node the includer still needs. BuildPassages now runs a first pass collecting every
        // static IncludePassageNode.Target across the whole file before deciding whether to
        // title-hoist ANY passage, and skips hoisting for passages in that set. The contrast passage
        // below has the identical shape but is never included, and should still hoist normally.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Includer"] = new StoryPassage("Includer", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.passage("letter1a", System.Array.Empty<StoryVar>());
                yield break;
            }
            private void passage2_Init()
            {
                base.Passages["letter1a"] = new StoryPassage("letter1a", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage2_Main));
            }
            private IEnumerable<StoryOutput> passage2_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Letter Heading");
                }
                yield return base.lineBreak();
                yield return base.text("Body text follows.");
                yield break;
            }
            private void passage3_Init()
            {
                base.Passages["Standalone"] = new StoryPassage("Standalone", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage3_Main));
            }
            private IEnumerable<StoryOutput> passage3_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Letter Heading");
                }
                yield return base.lineBreak();
                yield return base.text("Body text follows.");
                yield break;
            }
            """);

        var included = passages.Single(p => p.PassageId == "letter1a");
        Assert.Equal("letter1a", included.Title);
        Assert.Contains(included.Nodes, n => n is TextNode t && t.Template == "Letter Heading");

        var standalone = passages.Single(p => p.PassageId == "Standalone");
        Assert.Equal("Letter Heading", standalone.Title);
        Assert.DoesNotContain(standalone.Nodes, n => n is TextNode t && t.Template == "Letter Heading");
    }

    [Fact]
    public void Passage_TransitivelyTargetedByDynamicIncludePassage_DoesNotHoistTitle()
    {
        // Regression: Fear of the Unknown's AsylumHub/AsylumTest1/CountQuestion4. AsylumHub sets
        // `quest1 = "Question4"` (a plain string-literal assign); AsylumTest1 later does a DYNAMIC
        // include (base.passage(this.Vars.quest1, ...)) rather than naming the passage literally. No
        // single IncludePassageNode names "Question4" directly, so the static-target collector above
        // never protects it - without resolving the indirection through the shared `quest1`
        // variable, Question4's own leading bold heading was free to be hoisted into `title:`, which
        // include_passage never renders (it only ever splices Nodes, see PassageRenderer's
        // IncludePassageNode case) - silently deleting the question's own text from every render that
        // included it. BuildPassages now also collects every bare-identifier dynamic include target
        // var and every literal string ever assigned to it, and treats a match against a real
        // passage name the same as a static target.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Hub"] = new StoryPassage("Hub", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.quest1 = "Question4";
                yield break;
            }
            private void passage2_Init()
            {
                base.Passages["AsylumTest1"] = new StoryPassage("AsylumTest1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage2_Main));
            }
            private IEnumerable<StoryOutput> passage2_Main()
            {
                yield return base.passage(this.Vars.quest1, System.Array.Empty<StoryVar>());
                yield break;
            }
            private void passage3_Init()
            {
                base.Passages["Question4"] = new StoryPassage("Question4", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage3_Main));
            }
            private IEnumerable<StoryOutput> passage3_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Are you mentally ill?");
                }
                yield return base.lineBreak();
                yield return base.text("Body text follows.");
                yield break;
            }
            """);

        var included = passages.Single(p => p.PassageId == "Question4");
        Assert.Equal("Question4", included.Title);
        Assert.Contains(included.Nodes, n => n is TextNode t && t.Template == "Are you mentally ill?");
    }

    [Fact]
    public void Passage_TransitivelyTargetedByShuffledDynamicIncludePassage_DoesNotHoistTitle()
    {
        // Same bug class as the plain-literal-assign case above, but the indirection is a random
        // pick among several literal candidates instead of one fixed literal. Regression: Cost of
        // Disease's HuntNorth sets `HuntNorthnextPsg = ["Wight", "Moon Presence"].shuffled(key)[0]`
        // (a "choose-one" VarRandom, not a plain string VarSets assign - either() below is the
        // simplest Cradle source shape that produces the same extractor-internal VarRandom), then
        // includes it dynamically. Both "Wight" and "Moon Presence" need the same title-hoist
        // protection a single literal assign would get, since either one could be the actual target
        // at runtime.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Hub"] = new StoryPassage("Hub", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.nextMonster = this.macros1.either(new StoryVar[] { "Wight", "MoonPresence" });
                yield break;
            }
            private void passage2_Init()
            {
                base.Passages["HuntNorth"] = new StoryPassage("HuntNorth", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage2_Main));
            }
            private IEnumerable<StoryOutput> passage2_Main()
            {
                yield return base.passage(this.Vars.nextMonster, System.Array.Empty<StoryVar>());
                yield break;
            }
            private void passage3_Init()
            {
                base.Passages["Wight"] = new StoryPassage("Wight", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage3_Main));
            }
            private IEnumerable<StoryOutput> passage3_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("A wight emerges from the forest.");
                }
                yield return base.lineBreak();
                yield return base.text("Body text follows.");
                yield break;
            }
            """);

        var wight = passages.Single(p => p.PassageId == "Wight");
        Assert.Equal("Wight", wight.Title);
        Assert.Contains(wight.Nodes, n => n is TextNode t && t.Template == "A wight emerges from the forest.");
    }

    [Fact]
    public void Passage_TransitivelyTargetedByLetShuffledDynamicIncludePassage_DoesNotHoistTitle()
    {
        // Same bug class again, but the indirection var is a passage-scoped `let` (LetNode.Random)
        // rather than a session-global `Vars.x` assign (EffectNode.VarRandom) - Cost of Disease's
        // Barventures: `let _rnd_Barventures_1 = ["bar1", ..., "bar7"].shuffled(key)[0]`, immediately
        // followed by `include_passage: target: '${_rnd_Barventures_1}'` in the same switch case. An
        // inline either() passed directly as the include target produces exactly this LetNode.Random
        // shape (same as InlineEither_EmitsLetThenTemplate above, but feeding base.passage(...)
        // instead of base.text(...)).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Barventures"] = new StoryPassage("Barventures", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return base.passage(this.macros1.either(new StoryVar[] { "bar1", "bar2" }), System.Array.Empty<StoryVar>());
                yield break;
            }
            private void passage2_Init()
            {
                base.Passages["bar1"] = new StoryPassage("bar1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage2_Main));
            }
            private IEnumerable<StoryOutput> passage2_Main()
            {
                using (base.styleScope("bold", true))
                {
                    yield return base.text("Sworn Statement");
                }
                yield return base.lineBreak();
                yield return base.text("Body text follows.");
                yield break;
            }
            """);

        var bar1 = passages.Single(p => p.PassageId == "bar1");
        Assert.Equal("bar1", bar1.Title);
        Assert.Contains(bar1.Nodes, n => n is TextNode t && t.Template == "Sworn Statement");
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

    [Fact]
    public void EogSetupMarker_ComputedPassageName_PromotesToPopupTarget()
    {
        // Regression: A Time of War's Martial2 fragment (source line 4041-4046): "string passage =
        // Vars.warwinner == "Unified Monarchists" ? Vars.rumor2 == "visited" ? "AMessenger2" :
        // "AfternoonTea" : Vars.wardestroy == 1 ? "ATOWSabotageIntro2Sep" : "ATOWSabotageIntro2";
        // ViewEndOfGeneration.S_OnSetSpecialSetup?.Invoke("Special Setup", -1, passage, "...");" —
        // `passage` is a local var computed via nested ternary, not a literal. BuildEogSetupMarker
        // correctly tracked this as EogSetupMarkerNode.PassageNameNodes (a nested ConditionalNode
        // tree with LetNode{Var="passageName"} leaves), and TransformPopup already promoted that
        // tree into the popup's own content — but never used it to set the popup's target/onclose,
        // so the "Confirm"/"Close" button had nowhere to navigate. Fixed by collapsing
        // PassageNameNodes into a single ternary expression (via
        // PassageBodyVisitor.BuildTernaryExprFromLetConditionals, the same collapse already used to
        // hoist the local var's own `let`) and using it as the popup's target.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["Martial2"] = new StoryPassage("Martial2", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0000024"))
                    yield return base.link("Click here at the end of the round...", null, () => base.enchantHook("h0000024", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                string passage = Vars.warwinner == "Unified Monarchists" ? Vars.rumor2 == "visited" ? "AMessenger2" : "AfternoonTea" :
                    Vars.wardestroy == 1 ? "ATOWSabotageIntro2Sep" : "ATOWSabotageIntro2";
                ViewEndOfGeneration.S_OnSetSpecialSetup?.Invoke("Special Setup", -1, passage,
                    "Do NOT perform the End of Round actions at this time.");
                yield break;
            }
            """);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("popup", node["type"]);
        Assert.Equal("end_of_generation", node["layout"]);

        var target = (string)node["target"]!;
        Assert.StartsWith("${", target);
        Assert.Contains("warwinner", target);
        Assert.Contains("AMessenger2", target);
        Assert.Contains("AfternoonTea", target);
        Assert.Contains("ATOWSabotageIntro2Sep", target);
        Assert.Contains("ATOWSabotageIntro2", target);
        // Matches the sibling literal-passageName shape's own established wording (a target-bearing
        // end_of_generation popup uses "Close", not "Confirm" — "Confirm" is reserved for the
        // no-target case, see TransformPopup's own remarks).
        Assert.Equal("Close", node["okay"]);

        // The computed passageName conditional still lands in content too — TransformPopup already
        // inserted it there before this fix, and this fix doesn't remove it (the target expression
        // is a separate, self-contained collapse of the same tree, not a rewrite of this one).
        var content = (List<Dictionary<string, object?>>)node["content"]!;
        Assert.Contains(content, c => c["type"] as string == "conditional");
    }

    [Fact]
    public void EitherArgs_ConvertsEmbeddedRichTextTags()
    {
        // Regression: A Time of War's Sabotage1Now ("...must <b>discard 1 Experiment</b>..."),
        // PackingHeat1a/AdvancedWeaponryIntro/ReignHUB ("...1 <sprite=\"Creepy_Icon\" index=0>..."),
        // Fear of the Unknown's AsylumTreatmentB ("<i>illegible</i>") — a macros1.either() array
        // element sometimes has TextMesh Pro rich-text markup embedded directly in its own string
        // literal instead of being wrapped with styleScope()/a plain text() call. ExtractMacroArgs
        // used to extract these verbatim via LiteralValue, which has no rich-text handling of its
        // own (unlike ordinary text() calls, which always route through AddPlainTextRuns) — the raw
        // tags leaked straight into player-facing restext instead of MWS's own
        // **bold**/_italic_/{icon:...}.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.outcome = macros1.either("<i>illegible</i>", "and must <b>discard 1 Experiment</b> they completed", "1 <sprite=\"Creepy_Icon\" index=0> and 1 Resource");
                yield break;
            }
            """);

        var effect = Assert.Single(passages[0].Nodes.OfType<EffectNode>(), e => e.VarRandom is { Count: 1 });
        var values = effect.VarRandom!["outcome"].Values.Cast<string>().ToList();
        Assert.Contains("_illegible_", values);
        Assert.Contains("and must **discard 1 Experiment** they completed", values);
        Assert.Contains("1 {icon:creepy_icon} and 1 Resource", values);
        Assert.DoesNotContain(values, v => v.Contains('<'));
    }

    [Fact]
    public void DirectLiteralAssignment_ConvertsEmbeddedRichTextTags()
    {
        // Regression: A Time of War's ReignHUB assigns Vars.ch1/ch2 = "Lose 2
        // <sprite=\"Creepy_Icon\" index=0> ." for later display via {ch1}/{ch2} — a plain literal
        // assignment (ProcessAssignment's "Direct literal assignment" branch), which never routed
        // through rich-text conversion at all (unlike ordinary text() calls or either()/random()
        // choices), so the raw sprite tag leaked straight into restext.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.ch1 = "Lose 2 <sprite=\"Creepy_Icon\" index=0> .";
                yield break;
            }
            """);

        var effect = Assert.Single(passages[0].Nodes.OfType<EffectNode>(), e => e.VarSets is { Count: 1 });
        Assert.Equal("Lose 2 {icon:creepy_icon} .", effect.VarSets!["ch1"]);
    }

    [Fact]
    public void EitherArgs_ConcatenatedChoice_ConvertsEmbeddedRichTextTag()
    {
        // Regression: A Time of War's PackingHeat1a either() second choice is itself a
        // "+"-concatenated literal (Cradle line-wraps long strings): "1 Resource of their choice
        // and moves the <sprite=\"AngryMob_Icon\" index=0> Marke" + "r 1 space to the left." —
        // ExtractMacroArgs' non-array-literal branch falls through to TryBuildConcatString for a
        // concatenated argument, which used to return the merged string unconverted even though a
        // plain (non-concatenated) literal choice in the same call already went through
        // rich-text conversion.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.outcome = macros1.either("plain choice", "1 Resource of their choice and moves the <sprite=\"AngryMob_Icon\" index=0> Marke" +
                    "r 1 space to the left.");
                yield break;
            }
            """);

        var effect = Assert.Single(passages[0].Nodes.OfType<EffectNode>(), e => e.VarRandom is { Count: 1 });
        var values = effect.VarRandom!["outcome"].Values.Cast<string>().ToList();
        Assert.Contains("1 Resource of their choice and moves the {icon:angrymob_icon} Marker 1 space to the left.", values);
        Assert.DoesNotContain(values, v => v.Contains('<'));
    }

    [Fact]
    public void TextConcat_SpriteTagSplitAcrossPlusBoundary_MergesBeforeConverting()
    {
        // Regression: A Time of War's Reign6 fragment splits a sprite tag literally across a C#
        // line-continuation "+" with nothing (no variable, no either()) between the two literal
        // halves: "...<b>lose 2 <" + "sprite=\"Creepy_Icon\" index=0>.</b> You gain 2VP.</i>".
        // ProcessTextConcatPart used to convert each literal leaf independently via AddPlainTextRuns,
        // so neither leaf on its own looked like a complete tag to TryParseRichText — the fix flattens
        // the whole "+" chain first and merges adjacent literal leaves into one string before
        // converting, so the reassembled tag is seen whole.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                yield return text(" <i>This will result in: " + Vars.crownL + " All players <b>lose 2 <" +
                    "sprite=\"Creepy_Icon\" index=0>.</b> You gain 2VP.</i>");
                yield break;
            }
            """);

        // A lone text() call keeps its extractor-internal Runs shape (Template is only populated
        // by a later multi-node merge pass) — reconstruct the same way MwsNodes.TextNode.ToDict()
        // and CradleExtractor.BuildTemplate both do, via BuildValueFromRuns.
        var text = Assert.Single(passages[0].Nodes.OfType<TextNode>());
        var value = MwsExprHelper.BuildValueFromRuns(text.Runs);
        Assert.Contains("{icon:creepy_icon}", value);
        Assert.DoesNotContain("<", value);
        Assert.DoesNotContain("sprite=", value);
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
    public void StringConcatWithEither_EmitsLetThenTemplate()
    {
        // A string concatenation mixing literal text, a plain variable, and an either() call
        // builds a {var}-braced VarSets display template (same as the plain InlineEither_
        // EmitsLetThenTemplate case, just via VarSets instead of a TextNode). This used to crash
        // ("Unexpected trailing input: '{'") because the expression grammar had no {var}
        // interpolation and V2Serializer's VarSetStringToExpr left mixed-content strings
        // unquoted — both now fixed (interpolation added to ExpressionEvaluator; StringValueToExpr/
        // VarSetStringToExpr quote mixed-brace content), and using VarSets here (not VarMath) is
        // what lets RestextCollector promote the whole combining template to its own translatable
        // restext entry, with the individual either() choices staying translatable too since their
        // temp var now appears as a placeholder inside that same entry's value.
        // Real-world source: Fear of the Unknown's FearoftheUnknownStart/ProperLetterHeading
        // "newspaper" assign (Vars.newspaper = "The " + Vars.townname + " " +
        // macros1.either("Ledger", "Gazette", ...)).
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.newspaper = "The " + this.Vars.townname + " " + this.macros1.either("Ledger", "Gazette", "Mercury");
                yield break;
            }
            """);

        var nodes = passages[0].Nodes;
        var let = nodes.OfType<LetNode>().First();
        Assert.Equal("choose-one", let.Random!.RandomType);

        var effect = nodes.OfType<EffectNode>().First();
        Assert.Null(effect.VarMath);
        Assert.NotNull(effect.VarSets);
        var newspaperTemplate = effect.VarSets!["newspaper"] as string;
        Assert.Contains("{townname}", newspaperTemplate);
        Assert.Contains($"{{{let.Var}}}", newspaperTemplate);
        Assert.StartsWith("The ", newspaperTemplate);
    }

    [Fact]
    public void LocalIntListCountCheck_EmitsArrayCountMethodNotBareFunction()
    {
        // Regression: Fear of the Unknown's FPFateHub passage (source lines 16628-16663).
        // `fpfateList.Count > 0` — a local List<int> mirroring Vars.fpfate (matched via identical
        // literal contents, see _localIntLists/_passageIntArrayVars) — used to translate to
        // `count(fpfate) > 0`. The engine has no bare count(...) function (see ExpressionEvaluator.
        // EvalFunction — only rand_between/max/min/parseInt are registered), only the arr.count()
        // METHOD (EvalArrayMethod), so this threw "Unknown function 'count'" at evaluation time.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                this.Vars.fpfate = this.macros1.a(1, 2, 3, 4, 5, 6);
                List<int> fpfateList = new List<int>() { 1, 2, 3, 4, 5, 6 };
                if (fpfateList.Count > 0)
                {
                    this.Vars.plA = 1;
                }
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().Single();
        Assert.Equal("fpfate.count() > 0", cond.Branches[0].Condition);
    }

    [Fact]
    public void RedundantStringIsNullOrEmpty_CollapsesIntoCompoundFalsyNegation()
    {
        // Regression: Fear of the Unknown's PrivateHomeTile passage (source line 34908).
        // "bhome == 0 || bhome == "" || String.IsNullOrEmpty(bhome)" — the first two clauses were
        // already collapsed to !bhome by the compound-falsy rule, leaving "!bhome ||
        // String.IsNullOrEmpty(bhome)" untranslated (the engine has no "String" namespace/type at
        // all, so it threw "Unknown variable 'String'" at evaluation time). The IsNullOrEmpty clause
        // is fully redundant with !bhome — StoryValue.AsBool already treats "" as falsy for a
        // string, and there's no separate "null" StoryValue variant a variable could ever hold
        // beyond that — so it must be dropped, not translated into some real MWS call.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.bhome == 0 || this.Vars.bhome == "" || String.IsNullOrEmpty(this.Vars.bhome))
                {
                    this.Vars.plA = 1;
                }
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().Single();
        Assert.Equal("!bhome", cond.Branches[0].Condition);
    }

    [Fact]
    public void NegatedStringIsNullOrEmpty_CollapsesToBareTruthyCheck()
    {
        // Regression: A Time of War's newmeat check ("!String.IsNullOrEmpty(Vars.newmeat)"). Same
        // translation gap as RedundantStringIsNullOrEmpty_CollapsesIntoCompoundFalsyNegation, but
        // standalone rather than paired with an already-collapsed !x — "x is not null-or-empty"
        // reduces to plain "x" (truthy), same StoryValue.AsBool reasoning.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (!String.IsNullOrEmpty(this.Vars.newmeat))
                {
                    this.Vars.plA = 1;
                }
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().Single();
        Assert.Equal("newmeat", cond.Branches[0].Condition);
    }

    [Fact]
    public void NegatedStringIsNullOrEmpty_OnLocalAlias_ResolvesToUnderlyingVar()
    {
        // Regression: Fear of the Unknown's Player1Statsfin passage ("string s = Vars.warriorA.
        // ToString(); if (!String.IsNullOrEmpty(s)) ..."). The bare-identifier collapse from
        // NegatedStringIsNullOrEmpty_CollapsesToBareTruthyCheck emitted the raw local alias name
        // "s" verbatim — not a declared MWS variable at all, only "warriorA" is (see the "string
        // varName = Vars.X" declaration handling registering s as a {warriorA} placeholder) — so
        // this threw "Unknown variable 's'" at evaluation time. Must resolve through _localVars the
        // same way the sibling s.Replace(...) text-position handling already does.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("bold", true))
                {
                    string s = this.Vars.warriorA.ToString();
                    if (!String.IsNullOrEmpty(s))
                    {
                        yield return base.text("has a name");
                    }
                }
                yield break;
            }
            """);

        var cond = passages[0].Nodes.OfType<ConditionalNode>().Single();
        Assert.Equal("warriorA", cond.Branches[0].Condition);
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
    public void SetEndOfRound_DirectCall_SynthesizesAutoDisplayPopup()
    {
        // Regression: A Time of War's MartialPre3/Martia1Pre2 — ViewEndOfRound.instance.
        // SetEndOfRound(...) called directly at the top level of a passage (no CheckProgress, no
        // enclosing enchantHook link at all — the whole passage body is just this one call). The
        // extractor used to flatten this to a plain section+checkpoint+link (ModalNode/
        // TransformModal, predating the layout: end_of_round popup mechanism), so nothing ever set
        // `_ProgressRound` — hub_early/hub_middle/hub_late layouts, which read that variable, never
        // advanced round tracking for this occurrence. Fixed to synthesize the same auto-display
        // popup shape as the indirect CheckProgress-driven path (EndOfRoundMarkerNode), just built
        // directly since there's no enclosing link here to scan for a marker node.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["MartialPre3"] = new StoryPassage("MartialPre3", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                ViewEndOfRound.instance.SetEndOfRound("The Middle Years of the Second Generation has ended.", 5,
                    "Martial3", "Complete all End of Round Actions at this time.");
                yield return lineBreak();
                yield break;
            }
            """);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("popup", node["type"]);
        Assert.Equal("end_of_round", node["layout"]);
        // No label field at all — the end_of_round layout drives auto-display, matching
        // TransformEndOfGeneration's own established convention for the same "no trigger link in
        // source" shape.
        Assert.False(node.ContainsKey("label"));
        Assert.Equal("Martial3", node["target"]);
        Assert.Equal("End of Round", node["okay"]);

        var content = (List<Dictionary<string, object?>>)node["content"]!;
        Assert.Equal("The Middle Years of the Second Generation has ended.", content[0]["value"]);
        Assert.Equal("break", content[1]["type"]);
        Assert.Equal("paragraph", content[1]["style"]);
        Assert.Equal("Complete all End of Round Actions at this time.", content[2]["value"]);

        var onclose = (List<Dictionary<string, object?>>)node["onclose"]!;
        var assign = Assert.Single(onclose);
        Assert.Equal("assign", assign["type"]);
        Assert.Equal("_ProgressRound", assign["var"]);
        Assert.Equal("5", assign["expr"]);
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
    public void CheckProgress_TargetIsLocalTernaryAlsoReferencedInEarlierCondition_HoistsToSharedLet()
    {
        // Regression: A Time of War's TakeSides3 fragment (source line 2243-2258):
        // "string pass = (Vars.barracks != "yes") ? (Vars.gunsbonus == 0) ? Vars.war == 1 ?
        // "Warwarn" : "TowardsWar" : "SeedGUNS" : "TSBarracksPenalty"; if (pass == "Warwarn") { ... }
        // PassageTracker.instance.CheckProgress("TakeSides3", pass);" — `pass` is a local C# var
        // (not a Cradle session variable) referenced TWICE: once in an `if` condition, once as
        // CheckProgress's target. The declaration site used to suppress the assignment entirely
        // (tracked only in _localPassageConditionals, a node-tree used solely by
        // BuildEogSetupMarker/S_OnSetSpecialSetup) and CheckProgress's own resolution separately
        // re-derived/duplicated the ternary as a parallel tree of GotoNodes
        // (BuildGotoNodesFromLetConditionals) — leaving the EARLIER `if (pass == "Warwarn")`
        // condition referencing a variable that was never emitted anywhere, since nothing else ever
        // consulted _localPassageConditionals. Fix: the declaration site now also collapses the
        // ternary into one `let`, and CheckProgress resolution references it by name via a single
        // dynamic goto instead of re-deriving/duplicating the branch structure.
        var mapper = MakeProgressMapper("""{ "TakeSides3": { "progress": 3 } }""");
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["TakeSides3"] = new StoryPassage("TakeSides3", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0000009"))
                    yield return base.link("Click here at the End of the Generation...", null, () => base.enchantHook("h0000009", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                string pass = (this.Vars.barracks != "yes") ? (this.Vars.gunsbonus == 0) ? this.Vars.war == 1 ? "Warwarn" : "TowardsWar" : "SeedGUNS" : "TSBarracksPenalty";
                if (pass == "Warwarn")
                {
                    if (this.Vars.townname == "Paradox")
                    {
                        this.Vars.war = 1;
                    }
                }
                PassageTracker.instance.CheckProgress("TakeSides3", pass);
                yield break;
            }
            """, mapper, out _);

        var dict = V2Serializer.ToDict(passages[0]);
        var nodes = (List<Dictionary<string, object?>>)dict["nodes"]!;
        var link = Assert.Single(nodes, n => (string)n["type"]! == "link");

        // The trailing single-target goto folds directly into the link's own target — no leftover
        // onclick goto needed.
        Assert.Equal("${pass}", link["target"]);

        var onclick = (List<Dictionary<string, object?>>)link["onclick"]!;
        var letDict = Assert.Single(onclick, n => (string)n["type"]! == "let");
        Assert.Equal("pass", letDict["var"]);
        var expr = (string)letDict["expr"]!;
        Assert.DoesNotContain("either", expr);
        Assert.Contains("barracks", expr);
        Assert.Contains("Warwarn", expr);

        // The `let` must precede the condition that reads it, and the condition must reference the
        // real variable — not an untranslated bare identifier that was never declared anywhere.
        var letIdx = onclick.IndexOf(letDict);
        var condIdx = onclick.FindIndex(n => (string)n["type"]! == "conditional");
        Assert.True(letIdx >= 0 && letIdx < condIdx);
        Assert.Equal("pass == \"Warwarn\"", onclick[condIdx]["if"]);

        // No stray GotoNode-tree leftover from the old duplication.
        Assert.DoesNotContain(onclick, n => (string)n["type"]! == "goto");
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
    public void ExpandLink_AssignThenNonExhaustiveConditionalOfGotos_BecomesLinkWithUnreachableFallback()
    {
        // Superseded design decision — this used to assert "stays popup" (without a final `else`,
        // the conditional isn't provably exhaustive, so a goto-inside-onclick link could do nothing
        // on a click that matches no branch). But a fallback `layout: 'reveal'` popup exists to show
        // content before an exit link — this fragment has NONE (pure assign+conditional-of-goto, no
        // text/image/etc anywhere), so a reveal popup here is an empty box with nothing to reveal
        // and only a generic Close, strictly worse than a link. Real occurrence: Fear of the
        // Unknown's LiberalEvent2ab's first fragment (00353-LiberalEvent2ab.mws.yaml) — reported as
        // rendering an empty reveal popup instead of navigating. Fixed via IsLogicOnly: when content
        // has no renderable output at all, fall back to a link+onclick with an explicit
        // UnreachableTarget ("__UNREACHABLE__") rather than omitting target the way the exhaustive
        // case does — exhaustiveness isn't proven here, so target can't be safely omitted, but the
        // sentinel makes it obvious in logs/state if the theoretical gap is ever actually hit,
        // instead of a blank target that looks like an extraction bug.
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
        Assert.Equal("link", node["type"]);
        Assert.Equal("__UNREACHABLE__", node["target"]);

        var onclick = (List<Dictionary<string, object?>>)node["onclick"]!;
        Assert.Equal("assign", onclick[0]["type"]);
        Assert.Equal("conditional", onclick[1]["type"]);
    }

    [Fact]
    public void ExpandLink_AssignThenExhaustiveSwitchOfGotos_BecomesLinkWithOnclick()
    {
        // Regression: Masterwork-Modules/fear-of-the-unknown/passages/00203-GainFamilyPlot.mws.yaml.
        // Same idiom as ExpandLink_AssignThenExhaustiveIfElseOfGotos_BecomesLinkWithOnclick, but the
        // if/elseif/else chain here (3+ branches, single Vars.X == literal condition each) gets
        // folded into a SwitchNode by TryConvertCompoundConditionalToSwitch before this ever runs —
        // AlwaysNavigatesToGoto must recognize an exhaustive (has a default case) SwitchNode the same
        // way it already recognized an exhaustive ConditionalNode, or this still falls through to a
        // popup with no okay/close button that a goto inside content can never actually trigger.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                this.Vars.mobbed = "yes";
                if (this.Vars.round == 1)
                {
                    yield return base.abort(goToPassage: "Mania");
                }
                else if (this.Vars.round == 2)
                {
                    yield return base.abort(goToPassage: "Mania2");
                }
                else
                {
                    yield return base.abort(goToPassage: "Mania3");
                }
                yield break;
            }
            """);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("link", node["type"]);
        Assert.Equal("Click to continue...", node["label"]);
        Assert.False(node.ContainsKey("target"));

        var onclick = (List<Dictionary<string, object?>>)node["onclick"]!;
        Assert.Equal("assign", onclick[0]["type"]);
        Assert.Equal("switch", onclick[1]["type"]);

        var cases = (List<Dictionary<string, object?>>)onclick[1]["cases"]!;
        Assert.Equal(2, cases.Count);
        var case1Nodes = (List<Dictionary<string, object?>>)cases[0]["nodes"]!;
        Assert.Equal("goto", case1Nodes[0]["type"]);
        Assert.Equal("Mania", case1Nodes[0]["target"]);

        var defaultNodes = (List<Dictionary<string, object?>>)onclick[1]["default"]!;
        Assert.Equal("goto", defaultNodes[0]["type"]);
        Assert.Equal("Mania3", defaultNodes[0]["target"]);
    }

    [Fact]
    public void ExpandLink_AssignThenNonExhaustiveSwitchOfGotos_BecomesLinkWithUnreachableFallback()
    {
        // Superseded design decision, mirroring ExpandLink_AssignThenNonExhaustiveConditionalOfGotos_
        // BecomesLinkWithUnreachableFallback's own remarks: a switch with no default case isn't
        // provably exhaustive, but this fragment has no renderable content either (IsLogicOnly), so
        // it becomes a link+onclick with the UnreachableTarget sentinel instead of an empty reveal
        // popup.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click to continue...", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                this.Vars.mobbed = "yes";
                if (this.Vars.round == 1)
                {
                    yield return base.abort(goToPassage: "Mania");
                }
                else if (this.Vars.round == 2)
                {
                    yield return base.abort(goToPassage: "Mania2");
                }
                yield break;
            }
            """);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("link", node["type"]);
        Assert.Equal("__UNREACHABLE__", node["target"]);

        var onclick = (List<Dictionary<string, object?>>)node["onclick"]!;
        Assert.Equal("assign", onclick[0]["type"]);
        Assert.Equal("conditional", onclick[1]["type"]);
    }

    [Fact]
    public void ExpandLink_NestedNonExhaustiveOfGotos_BecomesLinkWithUnreachableFallback()
    {
        // Regression, the exact real-world shape: Fear of the Unknown's LiberalEvent2ab's first
        // fragment (00353-LiberalEvent2ab.mws.yaml) - `assign; if (creature==2) goto A; else { if
        // (lib=="taxes") goto B; if (lib=="nationalist") goto C; }`. The OUTER conditional has an
        // else (exhaustive at that level), but the else branch's own last node is a second, non-else
        // `if` - AlwaysNavigatesToGoto correctly reports false for the whole thing, since Cradle's
        // own `lib` values aren't statically known to be only "taxes"/"nationalist". But there's
        // nothing to reveal in a popup here either (IsLogicOnly), so this becomes a link+onclick with
        // the UnreachableTarget sentinel rather than an empty reveal popup - matches the sibling
        // (dynamic-target, exhaustive) fragment right next to it in the same passage, which already
        // correctly became a plain link with a real target.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                using (base.styleScope("hook", "h0002"))
                    yield return base.link("Click here if the bribe succeeded.", null, () => base.enchantHook("h0002", HarloweEnchantCommand.Replace, passage1_Fragment_0));
                yield break;
            }
            private IEnumerable<StoryOutput> passage1_Fragment_0()
            {
                this.Vars.bribe = "yes";
                if (this.Vars.creature == 2)
                {
                    yield return base.abort(goToPassage: "LiberalEventGood");
                }
                else
                {
                    if (this.Vars.lib == "taxes")
                    {
                        yield return base.abort(goToPassage: "LiberalEventA");
                    }
                    if (this.Vars.lib == "nationalist")
                    {
                        yield return base.abort(goToPassage: "LiberalEventB");
                    }
                }
                yield break;
            }
            """);

        var dict = V2Serializer.ToDict(passages[0]);
        var node = (Dictionary<string, object?>)((List<Dictionary<string, object?>>)dict["nodes"]!)[0];
        Assert.Equal("link", node["type"]);
        Assert.Equal("__UNREACHABLE__", node["target"]);

        var onclick = (List<Dictionary<string, object?>>)node["onclick"]!;
        Assert.Equal("assign", onclick[0]["type"]);
        Assert.Equal("conditional", onclick[1]["type"]);
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

    [Fact]
    public void InlineRandomInConditional_HoistsToLetBeforeConditional()
    {
        // Regression: Fear of the Unknown's BattleStart passage (source line 25848):
        // "if (macros1.random(1, 40) > Vars.tempagi)" — a to-hit combat roll. HoistInlineEithers
        // only recognized either() calls embedded in a condition, not random(min, max); this passed
        // straight through into the emitted `if:` expression untouched (SimplifyCondition only does
        // textual Vars.X normalization), and would have failed at render time with "Unknown variable
        // 'macros1'" the same way the either()-in-condition bug did before its own fix. Cradle draws
        // a fresh value every random() call, so — same as either() — it must be hoisted into its own
        // `let` right before the conditional, not left inline.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (macros1.random(1, 40) > this.Vars.tempagi)
                {
                    yield return base.text("hit");
                }
                else
                {
                    yield return base.text("miss");
                }
                yield break;
            }
            """);

        var let = Assert.Single(passages[0].Nodes.OfType<LetNode>());
        Assert.NotNull(let.Random);
        Assert.Equal("range", let.Random!.RandomType);
        Assert.Equal(1, let.Random.Min);
        Assert.Equal(40, let.Random.Max);

        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        var ifCond = cond.Branches.Single(b => b.Else != true).Condition!;
        Assert.DoesNotContain("random", ifCond);
        Assert.DoesNotContain("macros1", ifCond);
        Assert.Contains(let.Var, ifCond);
        Assert.Contains("tempagi", ifCond);

        var dict = V2Serializer.ToDict(passages[0]);
        var nodes = (List<Dictionary<string, object?>>)dict["nodes"]!;
        var letDict = Assert.Single(nodes, n => (string)n["type"]! == "let");
        Assert.Equal("rand_between(1, 40, \"P1_0\")", letDict["expr"]);

        var condDict = Assert.Single(nodes, n => (string)n["type"]! == "conditional");
        Assert.Equal($"{let.Var} > tempagi", condDict["if"]);
    }

    [Fact]
    public void HarloweOrdinalIndexerInConditional_ConvertsToZeroBasedIndex()
    {
        // Regression: Fear of the Unknown's BattleEnd passage (elim["1st"] == warriorA, source line
        // 26122). Harlowe's ordinal-string array indexer sugar ("x["1st"]" meaning "the first
        // element of x") already gets converted to a real zero-based integer index — x[0] — on an
        // assignment's RHS (see ProcessAssignment's ElementAccessExpressionSyntax case, via
        // HarloweOrdinalToIndex), but SimplifyCondition never applied the same conversion for the
        // identical shape appearing inside a condition, so it passed through as a literal quoted
        // string subscript — "elim[\"1st\"]" — which isn't valid MWS array indexing at all (a real
        // integer subscript, no string-key sugar exists), throwing "Cannot convert string to int"
        // once the engine tried to use it as an index.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                if (this.Vars.elim["1st"] == this.Vars.warriorA)
                {
                    yield return base.text("first eliminated was A");
                }
                yield break;
            }
            """);

        var cond = Assert.Single(passages[0].Nodes.OfType<ConditionalNode>());
        var ifCond = cond.Branches.Single(b => b.Else != true).Condition!;
        Assert.Equal("elim[0] == warriorA", ifCond);
    }

    [Fact]
    public void SetupPassagenameAssignedFromInlineEither_ResolvesInlineWithNoHoisting()
    {
        // Regression: A Time of War's RumorD1 passage (source line 1958-1960):
        // "ViewItemObtain.SetupPassagename = macros1.either("RumorIngredient", "RumorKnowledge",
        // "RumorPitch");" — a direct standalone assignment, no intermediate variable. Unlike the
        // condition-context either() bug (HoistInlineEithers), this is an assignment-RHS context
        // that IsSetupPassagenameAssignment's own fallback used to treat as generic "computed
        // expression" text, passing "macros1.either(...)" straight through SimplifyCondition (which
        // has no either()-hoisting logic of its own) into the emitted target — "${macros1.either(...)}"
        // — untranslated and unrenderable.
        //
        // Two earlier fixes hoisted the draw into an intermediate variable before landing here — a
        // `let` (reverted: a popup's `target` is resolved against the live VariableStore at close
        // time, after popup content's own mutations are committed, but that commit only ever copies
        // VariableStore._session, never the separate, transient ._let scope a `let` writes to — the
        // let-bound temp var rendered fine as popup content but threw "Unknown variable" the instant
        // the SAME popup's target tried to reference it after close), then a session `assign`
        // (worked, but polluted popup content with an unrelated statement and needed a broader
        // preamble-matching pattern elsewhere that caused its own regression — see git history).
        // Neither hoist was actually necessary: the draw expression is a pure function of that
        // visit's PRNG state, so SetupNotificationNode just carries the VarRandom directly and
        // V2Serializer resolves it inline wherever the target string is needed — no intermediate
        // node, no scope to cross.
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
            }
            private IEnumerable<StoryOutput> passage1_Main()
            {
                ViewItemObtain.SetupPassagename = macros1.either("RumorIngredient", "RumorKnowledge", "RumorPitch");
                yield break;
            }
            """);

        // No extra node of any kind — nothing to hoist.
        Assert.Empty(passages[0].Nodes.OfType<LetNode>());
        Assert.Empty(passages[0].Nodes.OfType<EffectNode>());

        var setup = Assert.Single(passages[0].Nodes.OfType<SetupNotificationNode>());
        Assert.Null(setup.NextPassage);
        Assert.NotNull(setup.Random);
        Assert.Equal("choose-one", setup.Random!.RandomType);
        Assert.Equal(["RumorIngredient", "RumorKnowledge", "RumorPitch"], setup.Random.Values);
        Assert.NotNull(setup.Random.SeedKey);

        var dict = V2Serializer.ToDict(passages[0]);
        var nodes = (List<Dictionary<string, object?>>)dict["nodes"]!;
        // No sibling "assign"/"let" node landed in the output — the draw is embedded directly.
        Assert.DoesNotContain(nodes, n => (string)n["type"]! is "assign" or "let");
        var linkDict = Assert.Single(nodes, n => (string)n["type"]! == "link");
        var target = (string)linkDict["target"]!;
        Assert.Contains(".shuffled(", target);
        Assert.DoesNotContain("macros1", target);
        Assert.DoesNotContain("either", target);
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
