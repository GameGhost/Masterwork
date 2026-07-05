using Masterwork.Engine;
using Masterwork.Engine.Expressions;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class PassageRendererTests
{
    private static (MwsPassageDoc passage, LoadedModule module, VariableStore store) Load(
        string mainYaml, string? variablesYaml = null, IEnumerable<string>? others = null, string mainId = "P1")
    {
        var yamls = new List<string> { mainYaml };
        if (others is not null)
        {
            yamls.AddRange(others);
        }

        var module = new ModuleLoader().LoadFromSources(yamls, variablesYaml);
        var store = new VariableStore(module.Variables, new SessionPrng(1));
        return (module.Passages[mainId], module, store);
    }

    private static PassageRenderResult Render(
        MwsPassageDoc passage, LoadedModule module, VariableStore store, ISet<string>? visited = null) =>
        new PassageRenderer().Render(passage, store, module, (IReadOnlySet<string>)(visited ?? new HashSet<string>()));

    // ── Text and structure ──────────────────────────────────────────────────

    [Fact]
    public void PlainText_EmitsRenderedText()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Hello world'
            """);

        var result = Render(passage, module, store);
        var text = Assert.IsType<RenderedText>(result.Nodes.Single());
        Assert.Equal("Hello world", text.Value);
    }

    [Fact]
    public void ResolvesTemplateVars_InText()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Hello {nameA}'
            """);
        store.SetSessionVariable("nameA", StoryValue.Of("Alice"));

        var result = Render(passage, module, store);
        Assert.Equal("Hello Alice", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Break_EmitsRenderedBreak()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'break'
            """);

        var result = Render(passage, module, store);
        Assert.IsType<RenderedBreak>(result.Nodes.Single());
    }

    [Fact]
    public void ParagraphBreak_EmitsRenderedParagraphBreak()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'paragraph_break'
            """);

        var result = Render(passage, module, store);
        Assert.IsType<RenderedParagraphBreak>(result.Nodes.Single());
    }

    [Fact]
    public void Section_EmitsRenderedSection()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'section'
              title: 'My Section'
              content:
              - type: 'text'
                value: 'body'
            """);

        var result = Render(passage, module, store);
        var section = Assert.IsType<RenderedSection>(result.Nodes.Single());
        Assert.Equal("My Section", section.Title);
        Assert.Single(section.Content);
    }

    [Fact]
    public void SectionWithNoTitle_EmitsRenderedSection()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'section'
              content: []
            """);

        var result = Render(passage, module, store);
        var section = Assert.IsType<RenderedSection>(result.Nodes.Single());
        Assert.Null(section.Title);
    }

    // ── Assignment ───────────────────────────────────────────────────────────

    [Fact]
    public void Assign_UpdatesSessionVariable()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '1'
            """);

        Render(passage, module, store);
        Assert.Equal(1L, store.GetVariable("round").AsInt());
    }

    [Fact]
    public void Assign_ExpressionEvaluated()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: 'round + 1'
            """);
        store.SetSessionVariable("round", StoryValue.Of(2L));

        Render(passage, module, store);
        Assert.Equal(3L, store.GetVariable("round").AsInt());
    }

    [Fact]
    public void MultipleAssigns_AllApplied()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'a'
              expr: '1'
            - type: 'assign'
              var: 'b'
              expr: '2'
            - type: 'assign'
              var: 'c'
              expr: '3'
            """);

        Render(passage, module, store);
        Assert.Equal(1L, store.GetVariable("a").AsInt());
        Assert.Equal(2L, store.GetVariable("b").AsInt());
        Assert.Equal(3L, store.GetVariable("c").AsInt());
    }

    [Fact]
    public void Assign_DoesNotEmitRenderedNode()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '1'
            """);

        var result = Render(passage, module, store);
        Assert.Empty(result.Nodes);
    }

    // ── Let ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Let_ComputedAndAvailableInText()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'let'
              var: 'x'
              expr: '1 + 2'
            - type: 'text'
              value: 'Value is {x}'
            """);

        var result = Render(passage, module, store);
        Assert.Equal("Value is 3", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Let_RandomExpr_IsDeterministic()
    {
        var yaml = """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'let'
              var: 'x'
              expr: 'rand_between(1, 100, "k")'
            - type: 'text'
              value: '{x}'
            """;

        var (p1, m1, s1) = Load(yaml);
        var r1 = Render(p1, m1, s1);

        var (p2, m2, s2) = Load(yaml);
        var r2 = Render(p2, m2, s2);

        Assert.Equal(
            Assert.IsType<RenderedText>(r1.Nodes.Single()).Value,
            Assert.IsType<RenderedText>(r2.Nodes.Single()).Value);
    }

    [Fact]
    public void Let_DoesNotPersistToSession()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'let'
              var: 'x'
              expr: '5'
            """);

        Render(passage, module, store);
        Assert.DoesNotContain("x", store.SessionSnapshot().Keys);
    }

    // ── Conditionals ─────────────────────────────────────────────────────────

    [Fact]
    public void Conditional_TrueBranch_Rendered()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'conditional'
              if: 'round == 1'
              then:
              - type: 'text'
                value: 'first round'
            """);
        store.SetSessionVariable("round", StoryValue.Of(1L));

        var result = Render(passage, module, store);
        Assert.Equal("first round", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Conditional_FalseBranch_Skipped()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'conditional'
              if: 'round == 1'
              then:
              - type: 'text'
                value: 'first round'
            """);
        store.SetSessionVariable("round", StoryValue.Of(2L));

        var result = Render(passage, module, store);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void Conditional_MultiBranch_FirstTrue_Wins()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'conditional'
              conditions:
              - if: 'round == 1'
                then:
                - type: 'text'
                  value: 'one'
              - if: 'round == 2'
                then:
                - type: 'text'
                  value: 'two'
            """);
        store.SetSessionVariable("round", StoryValue.Of(2L));

        var result = Render(passage, module, store);
        Assert.Equal("two", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Conditional_Else_RenderedWhenAllFalse()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'conditional'
              conditions:
              - if: 'round == 1'
                then:
                - type: 'text'
                  value: 'one'
              else:
              - type: 'text'
                value: 'other'
            """);
        store.SetSessionVariable("round", StoryValue.Of(9L));

        var result = Render(passage, module, store);
        Assert.Equal("other", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Conditional_Nested()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'conditional'
              if: 'a == 1'
              then:
              - type: 'conditional'
                if: 'b == 1'
                then:
                - type: 'text'
                  value: 'inner'
            """);
        store.SetSessionVariable("a", StoryValue.Of(1L));
        store.SetSessionVariable("b", StoryValue.Of(1L));

        var result = Render(passage, module, store);
        Assert.Equal("inner", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    // ── Switch ───────────────────────────────────────────────────────────────

    [Fact]
    public void Switch_MatchingCase_Executed()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'switch'
              on: 'players'
              cases:
              - match: 3
                nodes:
                - type: 'text'
                  value: 'three players'
            """);
        store.SetSessionVariable("players", StoryValue.Of(3L));

        var result = Render(passage, module, store);
        Assert.Equal("three players", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Switch_DefaultCase_WhenNoMatch()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'switch'
              on: 'players'
              cases:
              - match: 3
                nodes:
                - type: 'text'
                  value: 'three'
              default:
              - type: 'text'
                value: 'other'
            """);
        store.SetSessionVariable("players", StoryValue.Of(99L));

        var result = Render(passage, module, store);
        Assert.Equal("other", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Switch_PatternMatch_GreaterThan()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'switch'
              on: 'players'
              cases:
              - match: '>3'
                nodes:
                - type: 'text'
                  value: 'many'
            """);
        store.SetSessionVariable("players", StoryValue.Of(4L));

        var result = Render(passage, module, store);
        Assert.Equal("many", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Switch_NoMatch_NoDefault_ProducesNothing()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'switch'
              on: 'players'
              cases:
              - match: 3
                nodes:
                - type: 'text'
                  value: 'three'
            """);
        store.SetSessionVariable("players", StoryValue.Of(99L));

        var result = Render(passage, module, store);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void Switch_ExecutesAssignInCase()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'switch'
              on: 'players'
              cases:
              - match: 2
                nodes:
                - type: 'assign'
                  var: 'tracker'
                  expr: '6'
            """);
        store.SetSessionVariable("players", StoryValue.Of(2L));

        Render(passage, module, store);
        Assert.Equal(6L, store.GetVariable("tracker").AsInt());
    }

    // ── Foreach ──────────────────────────────────────────────────────────────

    [Fact]
    public void Foreach_IteratesAllElements()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'foreach'
              var: 'entry'
              in: 'names'
              do:
              - type: 'text'
                value: '{entry}'
            """);
        store.SetSessionVariable("names", StoryValue.Of(new List<StoryValue> { StoryValue.Of("a"), StoryValue.Of("b"), StoryValue.Of("c") }));

        var result = Render(passage, module, store);
        Assert.Equal(["a", "b", "c"], result.Nodes.Cast<RenderedText>().Select(t => t.Value));
    }

    [Fact]
    public void Foreach_LoopVarAvailable()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'foreach'
              var: 'entry'
              in: 'players_ranked'
              do:
              - type: 'text'
                value: '{entry.player_name}'
            """);
        var record = new StoryValue.RecordVal(new Dictionary<string, StoryValue> { ["player_name"] = StoryValue.Of("Alice") });
        store.SetSessionVariable("players_ranked", StoryValue.Of(new List<StoryValue> { record }));

        var result = Render(passage, module, store);
        Assert.Equal("Alice", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void Foreach_EmptyArray_ProducesNothing()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'foreach'
              var: 'entry'
              in: 'names'
              do:
              - type: 'text'
                value: '{entry}'
            """);
        store.SetSessionVariable("names", StoryValue.Of(new List<StoryValue>()));

        var result = Render(passage, module, store);
        Assert.Empty(result.Nodes);
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    [Fact]
    public void Navigation_EmitsRenderedNavigation()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'navigation'
              label: 'Click here'
              target: 'P2'
              state_affecting: true
            """);

        var result = Render(passage, module, store);
        var nav = Assert.IsType<RenderedNavigation>(result.Nodes.Single());
        Assert.Equal("Click here", nav.Label);
        Assert.Equal("P2", nav.Target);
        Assert.True(nav.StateAffecting);
    }

    [Fact]
    public void Navigation_DynamicTarget_ResolvedAtFollowTime()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'navigation'
              label: 'Click here'
              target: '${nextPsg}'
              state_affecting: true
            """);
        store.SetSessionVariable("nextPsg", StoryValue.Of("SomeOtherPassage"));

        var result = Render(passage, module, store);
        var nav = Assert.IsType<RenderedNavigation>(result.Nodes.Single());
        Assert.Equal("${nextPsg}", nav.Target);
    }

    [Fact]
    public void Navigation_AddedToActions()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'navigation'
              label: 'Click here'
              target: 'P2'
              state_affecting: true
            """);

        var result = Render(passage, module, store);
        Assert.Single(result.Actions);
        Assert.IsType<RenderedNavigation>(result.Actions.Single());
    }

    // ── Popup ────────────────────────────────────────────────────────────────

    [Fact]
    public void Popup_WithLabel_EmitsWithLabelAndAutoDisplayFalse()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              label: 'Open'
              state_affecting: true
              content: []
            """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());
        Assert.Equal("Open", popup.Label);
        Assert.False(popup.AutoDisplay);
    }

    [Fact]
    public void Popup_NoLabel_EmitsWithAutoDisplayTrue()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              state_affecting: true
              content: []
            """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());
        Assert.Null(popup.Label);
        Assert.True(popup.AutoDisplay);
    }

    [Fact]
    public void Popup_ContentNotEvaluatedAtRender()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              label: 'Open'
              state_affecting: true
              content:
              - type: 'assign'
                var: 'round'
                expr: '99'
            """);
        store.SetSessionVariable("round", StoryValue.Of(1L));

        Render(passage, module, store);
        Assert.Equal(1L, store.GetVariable("round").AsInt());
    }

    [Fact]
    public void Popup_LayoutField_Preserved()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              layout: 'setup'
              state_affecting: true
              content: []
            """);

        var result = Render(passage, module, store);
        Assert.Equal("setup", Assert.IsType<RenderedPopup>(result.Nodes.Single()).Layout);
    }

    [Fact]
    public void Popup_NoContent_EmitsEmptyContentList()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              state_affecting: true
            """);

        var result = Render(passage, module, store);
        Assert.Empty(Assert.IsType<RenderedPopup>(result.Nodes.Single()).RawContent);
    }

    // ── Goto ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Goto_DoesNotEmitRenderedNode()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'goto'
              target: 'P2'
            """);

        var result = Render(passage, module, store);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void Goto_StopsRendering()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'goto'
              target: 'P2'
            - type: 'text'
              value: 'should not appear'
            """);

        var result = Render(passage, module, store);
        Assert.Empty(result.Nodes);
    }

    [Fact]
    public void Goto_TargetReturned()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'goto'
              target: 'P2'
            """);

        var result = Render(passage, module, store);
        Assert.Equal("P2", result.PendingGoto);
    }

    // ── Include passage ──────────────────────────────────────────────────────

    [Fact]
    public void IncludePassage_InlinesTargetPassage()
    {
        var (passage, module, store) = Load(
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'include_passage'
              target: 'P2'
            """,
            others:
            [
                """
                format: 'mws/0.3'
                passage_id: 'P2'
                layout: 'narration'
                nodes:
                - type: 'text'
                  value: 'from P2'
                """,
            ]);

        var result = Render(passage, module, store);
        Assert.Equal("from P2", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void IncludePassage_InheritsVariableStore()
    {
        var (passage, module, store) = Load(
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'include_passage'
              target: 'P2'
            - type: 'text'
              value: '{x}'
            """,
            others:
            [
                """
                format: 'mws/0.3'
                passage_id: 'P2'
                layout: 'narration'
                nodes:
                - type: 'let'
                  var: 'x'
                  expr: '42'
                """,
            ]);

        var result = Render(passage, module, store);
        Assert.Equal("42", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    // ── Location header ──────────────────────────────────────────────────────

    [Fact]
    public void LocationHeader_ExposedInResult()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'hub'
            location:
              name: 'The Hospital'
              icon: 'icon://hospital_icon'
            nodes: []
            """);

        var result = Render(passage, module, store);
        Assert.Equal("The Hospital", result.LocationName);
        Assert.Equal("icon://hospital_icon", result.LocationIcon);
    }

    [Fact]
    public void NoLocation_IsNull()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes: []
            """);

        var result = Render(passage, module, store);
        Assert.Null(result.LocationName);
    }

    // ── Check progress ───────────────────────────────────────────────────────

    [Fact]
    public void CheckProgress_PassedPassage_Renders()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            check_progress: 'Prereq'
            nodes:
            - type: 'text'
              value: 'ok'
            """);

        var result = Render(passage, module, store, visited: new HashSet<string> { "Prereq" });
        Assert.Equal("ok", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void CheckProgress_NotVisited_Throws()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            check_progress: 'Prereq'
            nodes:
            - type: 'text'
              value: 'ok'
            """);

        Assert.Throws<CheckProgressViolationException>(() => Render(passage, module, store));
    }
}
