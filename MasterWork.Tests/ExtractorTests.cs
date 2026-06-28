using System.Collections.Generic;
using System.Linq;
using MasterWork.Extractor;
using MasterWork.ModuleFormat;
using Xunit;

namespace MasterWork.Tests;

public class ExtractorTests
{
    private static List<MwsPassage> Extract(string source)
    {
        var tempFile = System.IO.Path.GetTempFileName() + ".cs";
        System.IO.File.WriteAllText(tempFile, source);
        try
        {
            var opts = new ExtractionOptions { InputDir = tempFile, OutputDir = "", IncludeDebug = true };
            var report = new ExtractionReport();
            var extractor = new CradleExtractor(opts, SpriteMapper.Empty(), report);
            return extractor.Extract([tempFile]);
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
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
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
        var passages = Extract("""
            private void passage1_Init()
            {
                base.Passages["P1"] = new StoryPassage("P1", new string[] { }, new Func<IEnumerable<StoryOutput>>(this.passage1_Main));
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
        Assert.Equal(9.0, rand.Min);
        Assert.Equal(11.0, rand.Max);
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
        Assert.Equal("{direction}", inc.Target);
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
        Assert.Equal("effect.first()", effect.VarSets!["tempeffect"]);
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
}
