using Masterwork.Engine;
using Masterwork.Engine.Expressions;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class PassageRendererTests
{
    private static (MwsPassageDoc passage, LoadedModule module, VariableStore store) Load(
        string mainYaml, string? variablesYaml = null, IEnumerable<string>? others = null, string mainId = "P1",
        IEnumerable<string>? layoutChromeYamls = null)
    {
        var yamls = new List<string> { mainYaml };
        if (others is not null)
        {
            yamls.AddRange(others);
        }

        var module = new ModuleLoader().LoadFromSources(yamls, variablesYaml, layoutChromeYamls: layoutChromeYamls);
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
    public void ParagraphStyleBreak_EmitsRenderedBreakWithStyle()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'break'
              style: 'paragraph'
            """);

        var result = Render(passage, module, store);
        var brk = Assert.IsType<RenderedBreak>(result.Nodes.Single());
        Assert.Equal("paragraph", brk.Style);
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
    public void Navigation_EmitsRenderedLink()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Click here'
              target: 'P2'
              snapshot: true
            """);

        var result = Render(passage, module, store);
        var nav = Assert.IsType<RenderedLink>(result.Nodes.Single());
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
            - type: 'link'
              label: 'Click here'
              target: '${nextPsg}'
              snapshot: true
            """);
        store.SetSessionVariable("nextPsg", StoryValue.Of("SomeOtherPassage"));

        var result = Render(passage, module, store);
        var nav = Assert.IsType<RenderedLink>(result.Nodes.Single());
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
            - type: 'link'
              label: 'Click here'
              target: 'P2'
              snapshot: true
            """);

        var result = Render(passage, module, store);
        Assert.Single(result.Actions);
        Assert.IsType<RenderedLink>(result.Actions.Single());
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
              snapshot: true
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
              snapshot: true
              content: []
            """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());
        Assert.Null(popup.Label);
        Assert.True(popup.AutoDisplay);
    }

    [Fact]
    public void Popup_ContentEvaluatedEagerlyAgainstSandbox_NotLiveStore()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              label: 'Open'
              snapshot: true
              content:
              - type: 'assign'
                var: 'round'
                expr: '99'
            """);
        store.SetSessionVariable("round", StoryValue.Of(1L));

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());

        // Evaluated eagerly, at render time, into the popup's own sandbox...
        Assert.Equal(99L, popup.Sandbox.GetVariable("round").AsInt());
        // ...but the live store stays untouched until the popup is actually accepted.
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
              snapshot: true
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
              snapshot: true
            """);

        var result = Render(passage, module, store);
        Assert.Empty(Assert.IsType<RenderedPopup>(result.Nodes.Single()).Content);
    }

    [Fact]
    public void Popup_NoHeader_EmitsEmptyHeaderList()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: true
              content: []
            """);

        var result = Render(passage, module, store);
        Assert.Empty(Assert.IsType<RenderedPopup>(result.Nodes.Single()).Header);
    }

    [Fact]
    public void Popup_HeaderAndContent_RenderIntoDistinctLists()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: true
              header:
              - type: 'image'
                asset: 'image://setup/StorybookToken'
                style: 'setup-image'
              content:
              - type: 'text'
                value: 'body text'
            """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());

        var headerImage = Assert.IsType<RenderedImage>(Assert.Single(popup.Header));
        Assert.Equal("image://setup/StorybookToken", headerImage.Asset);
        var contentText = Assert.IsType<RenderedText>(Assert.Single(popup.Content));
        Assert.Equal("body text", contentText.Value);
    }

    [Fact]
    public void Popup_HeaderAndContentInputs_GetNonCollidingActionIds()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: true
              header:
              - type: 'input'
                label: 'Header input'
                var: 'headerVar'
              content:
              - type: 'input'
                label: 'Content input'
                var: 'contentVar'
            """,
            variablesYaml: """
                standard_variables: []
                variables:
                  headerVar:
                    type: 'string'
                    default: ''
                  contentVar:
                    type: 'string'
                    default: ''
                """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());

        var headerInputId = Assert.IsType<RenderedInput>(Assert.Single(popup.Header)).Id;
        var contentInputId = Assert.IsType<RenderedInput>(Assert.Single(popup.Content)).Id;
        Assert.NotEqual(headerInputId, contentInputId);
        Assert.Equal(2, popup.Actions.Count);
    }

    [Fact]
    public void Input_NoLabelField_ParsesAndRendersWithEmptyLabel()
    {
        // label is optional — a module can render the visible label itself as a separate `text`
        // node beside the field instead (e.g. Cost of Disease's score-entry passages, which show
        // the player's name via its own `text` node ahead of an unlabeled `input`).
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Alice'
            - type: 'input'
              var: 'scoreA'
              min: 0
            """,
            variablesYaml: """
                standard_variables: []
                variables:
                  scoreA:
                    type: 'int'
                    default: 0
                """);

        var result = Render(passage, module, store);
        var input = Assert.IsType<RenderedInput>(result.Actions.Single());
        Assert.Equal("", input.Label);
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

    // ── Title / subtitle header ────────────────────────────────────────────────

    [Fact]
    public void TitleAndSubtitle_ExposedInResult()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            title: 'YELLOW FEVER'
            subtitle: 'Early Years'
            layout: 'hub'
            nodes: []
            """);

        var result = Render(passage, module, store);
        Assert.Equal("YELLOW FEVER", result.Title);
        Assert.Equal("Early Years", result.Subtitle);
    }

    [Fact]
    public void NoTitle_IsNull()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes: []
            """);

        var result = Render(passage, module, store);
        Assert.Null(result.Title);
        Assert.Null(result.Subtitle);
    }

    [Fact]
    public void Title_ExpandsVariableTemplates()
    {
        var (passage, module, store) = Load(
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            title: 'Hello {townname}'
            layout: 'hub'
            nodes: []
            """,
            variablesYaml: """
                standard_variables: []
                variables:
                  townname:
                    type: 'string'
                    default: 'Millbrook'
                """);

        var result = Render(passage, module, store);
        Assert.Equal("Hello Millbrook", result.Title);
    }

    // ── Layout chrome ────────────────────────────────────────────────────────

    [Fact]
    public void MatchingLayoutChrome_RendersIntoFourDistinctRegions()
    {
        var (passage, module, store) = Load(
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'hub_early'
            nodes:
            - type: 'text'
              value: 'body'
            """,
            layoutChromeYamls:
            [
                """
                format: 'mws/0.4'
                layout_id: 'hub_early'
                header:
                - type: 'text'
                  value: 'header text'
                footer:
                - type: 'text'
                  value: 'footer text'
                before_content:
                - type: 'text'
                  value: 'before text'
                after_content:
                - type: 'text'
                  value: 'after text'
                """,
            ]);

        var result = Render(passage, module, store);

        Assert.Equal("header text", Assert.IsType<RenderedText>(result.Chrome.Header.Single()).Value);
        Assert.Equal("footer text", Assert.IsType<RenderedText>(result.Chrome.Footer.Single()).Value);
        Assert.Equal("before text", Assert.IsType<RenderedText>(result.Chrome.BeforeContent.Single()).Value);
        Assert.Equal("after text", Assert.IsType<RenderedText>(result.Chrome.AfterContent.Single()).Value);
        Assert.Equal("body", Assert.IsType<RenderedText>(result.Nodes.Single()).Value);
    }

    [Fact]
    public void NoMatchingLayoutChrome_ChromeIsEmpty()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes: []
            """);

        var result = Render(passage, module, store);

        Assert.Empty(result.Chrome.Header);
        Assert.Empty(result.Chrome.Footer);
        Assert.Empty(result.Chrome.BeforeContent);
        Assert.Empty(result.Chrome.AfterContent);
    }

    [Fact]
    public void LayoutChromeLink_ActionMergedIntoPassageActionsWithNonCollidingId()
    {
        var (passage, module, store) = Load(
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'hub_early'
            nodes:
            - type: 'link'
              label: 'Body link'
              target: 'P1'
              snapshot: false
            """,
            layoutChromeYamls:
            [
                """
                format: 'mws/0.4'
                layout_id: 'hub_early'
                header:
                - type: 'link'
                  label: 'Chrome link'
                  target: 'P1'
                  snapshot: false
                """,
            ]);

        var result = Render(passage, module, store);

        var chromeLink = Assert.IsType<RenderedLink>(result.Chrome.Header.Single());
        var bodyLink = Assert.IsType<RenderedLink>(result.Nodes.Single());
        Assert.NotEqual(chromeLink.Id, bodyLink.Id);
        Assert.Contains(result.Actions, a => a.Id == chromeLink.Id);
        Assert.Contains(result.Actions, a => a.Id == bodyLink.Id);
    }

    [Fact]
    public void PopupWithMatchingLayoutChrome_RendersAgainstSandboxAndMergesActions()
    {
        var (passage, module, store) = Load(
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              layout: 'setup'
              snapshot: true
              content: []
            """,
            layoutChromeYamls:
            [
                """
                format: 'mws/0.4'
                layout_id: 'setup'
                header:
                - type: 'link'
                  label: 'Chrome link'
                  target: 'P1'
                  snapshot: false
                """,
            ]);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());

        var chromeLink = Assert.IsType<RenderedLink>(popup.Chrome.Header.Single());
        Assert.Contains(popup.Actions, a => a.Id == chromeLink.Id);
        // Popup chrome is scoped to the popup, not merged into the outer passage's own Actions.
        Assert.DoesNotContain(result.Actions, a => a.Id == chromeLink.Id);
    }

    [Fact]
    public void PopupWithNoLayout_ChromeIsEmpty()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: true
              content: []
            """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());

        Assert.Empty(popup.Chrome.Header);
        Assert.Empty(popup.Chrome.Footer);
        Assert.Empty(popup.Chrome.BeforeContent);
        Assert.Empty(popup.Chrome.AfterContent);
    }

    // ── Audio ────────────────────────────────────────────────────────────────

    [Fact]
    public void PassageAudio_LiteralMusicAndOnDisplay_ResolveAsIs()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            audio:
              music: 'audio://bgm/hospital_theme'
              on_display: 'audio://sfx/page_turn'
              on_display_delay_ms: 150
            nodes: []
            """);

        var result = Render(passage, module, store);
        Assert.Equal("audio://bgm/hospital_theme", result.Music);
        Assert.Equal("audio://sfx/page_turn", result.OnDisplaySound);
        Assert.Equal(150, result.OnDisplaySoundDelayMs);
    }

    [Fact]
    public void PassageAudio_Absent_ResolvesToNull()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes: []
            """);

        var result = Render(passage, module, store);
        Assert.Null(result.Music);
        Assert.Null(result.OnDisplaySound);
    }

    [Fact]
    public void PassageAudio_EmptyMusic_ResolvesToEmptyStringNotNull()
    {
        // The tri-state must survive resolution: "" (explicit silence) is not the same as
        // absent (inherit) — collapsing them would silently break the "topmost wins" stack.
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            audio:
              music: ''
            nodes: []
            """);

        var result = Render(passage, module, store);
        Assert.Equal("", result.Music);
    }

    [Fact]
    public void PassageAudio_ExpressionMusic_EvaluatedAtRenderTime()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            audio:
              music: '${voiceGender == "female" ? "audio://bgm/theme_f" : "audio://bgm/theme_m"}'
            nodes: []
            """, variablesYaml: """
            standard_variables: []
            variables:
              voiceGender:
                type: 'string'
                default: 'female'
            """);

        var result = Render(passage, module, store);
        Assert.Equal("audio://bgm/theme_f", result.Music);
    }

    [Fact]
    public void LinkAudio_ClickAndDelay_ResolveOntoRenderedLink()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'go'
              target: 'P2'
              audio:
                click: 'audio://sfx/ominous_click'
                click_delay_ms: 5
            """);

        var result = Render(passage, module, store);
        var link = Assert.IsType<RenderedLink>(result.Nodes.Single());
        Assert.Equal("audio://sfx/ominous_click", link.ClickSfx);
        Assert.Equal(5, link.ClickSfxDelayMs);
    }

    [Fact]
    public void LinkAudio_Absent_ResolvesToNullClickSfx()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'go'
              target: 'P2'
            """);

        var result = Render(passage, module, store);
        var link = Assert.IsType<RenderedLink>(result.Nodes.Single());
        Assert.Null(link.ClickSfx);
    }

    [Fact]
    public void PopupAudio_AllFields_ResolveOntoRenderedPopup()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              content: []
              audio:
                music: 'audio://bgm/tension_sting'
                open: 'audio://sfx/popup_open_dramatic'
                open_delay_ms: 10
                okay: 'audio://sfx/confirm'
                okay_delay_ms: 20
                cancel: ''
                cancel_delay_ms: 30
            """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());
        Assert.NotNull(popup.Audio);
        Assert.Equal("audio://bgm/tension_sting", popup.Audio!.Music);
        Assert.Equal("audio://sfx/popup_open_dramatic", popup.Audio.Open);
        Assert.Equal(10, popup.Audio.OpenDelayMs);
        Assert.Equal("audio://sfx/confirm", popup.Audio.Okay);
        Assert.Equal(20, popup.Audio.OkayDelayMs);
        Assert.Equal("", popup.Audio.Cancel);
        Assert.Equal(30, popup.Audio.CancelDelayMs);
    }

    [Fact]
    public void PopupAudio_AbsentAudioBlock_ResolvesToNullAudio()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              content: []
            """);

        var result = Render(passage, module, store);
        var popup = Assert.IsType<RenderedPopup>(result.Nodes.Single());
        Assert.Null(popup.Audio);
    }

    [Fact]
    public void AudioTrack_EmitsRenderedAudioTrackWithResolvedFields()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'audio_track'
              asset: 'audio://vo/greeting'
              title: 'Narration'
              style: 'narration-inline'
              autoplay: 500
              bgm_behavior: 'duck'
            """);

        var result = Render(passage, module, store);
        var track = Assert.IsType<RenderedAudioTrack>(result.Nodes.Single());
        Assert.Equal("audio://vo/greeting", track.Asset);
        Assert.Equal("Narration", track.Title);
        Assert.Equal("narration-inline", track.Style);
        Assert.True(track.Autoplay);
        Assert.Equal(500, track.AutoplayDelayMs);
        Assert.Equal("duck", track.BgmBehavior);
    }

    [Fact]
    public void AudioTrack_ExpressionAsset_EvaluatedAtRenderTime()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'audio_track'
              asset: '${voiceGender == "female" ? "audio://vo/greeting_f" : "audio://vo/greeting_m"}'
            """, variablesYaml: """
            standard_variables: []
            variables:
              voiceGender:
                type: 'string'
                default: 'male'
            """);

        var result = Render(passage, module, store);
        var track = Assert.IsType<RenderedAudioTrack>(result.Nodes.Single());
        Assert.Equal("audio://vo/greeting_m", track.Asset);
    }

    [Fact]
    public void AudioTrack_DefaultAutoplayAndBgmBehavior_NotOverridden()
    {
        var (passage, module, store) = Load("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'audio_track'
              asset: 'audio://vo/greeting'
            """);

        var result = Render(passage, module, store);
        var track = Assert.IsType<RenderedAudioTrack>(result.Nodes.Single());
        Assert.True(track.Autoplay);
        Assert.Null(track.AutoplayDelayMs);
        Assert.Equal("pause", track.BgmBehavior);
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
