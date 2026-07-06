using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class DeserializerTests
{
    private static MwsPassageDoc ParseOne(string yaml) =>
        new ModuleLoader().LoadFromSources([yaml]).Passages.Values.Single();

    [Fact]
    public void TextPassage_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Hello world'
            """);

        var text = Assert.IsType<TextNode>(passage.Nodes.Single());
        Assert.Equal("Hello world", text.Value);
    }

    [Fact]
    public void AllNodeTypes_Deserialize()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'hi'
            - type: 'break'
            - type: 'paragraph_break'
            - type: 'image'
              asset: 'icon://foo'
            - type: 'let'
              var: 'x'
              expr: '1'
            - type: 'assign'
              var: 'y'
              expr: '2'
            - type: 'navigation'
              label: 'go'
              target: 'P2'
              state_affecting: true
            - type: 'popup'
              label: 'open'
              content: []
              state_affecting: true
            - type: 'input'
              label: 'ask'
              text: 'enter'
              input: 'string'
              var: 'z'
              onsubmit: 'P2'
            - type: 'goto'
              target: 'P2'
            - type: 'include_passage'
              target: 'P2'
            - type: 'section'
              content: []
            - type: 'conditional'
              if: 'x == 1'
              then: []
            - type: 'switch'
              on: 'x'
              cases: []
            - type: 'foreach'
              var: 'e'
              in: 'arr'
              do: []
            - type: 'checkpoint'
              id: 'cp1'
            - type: 'record'
              id: 'ach1'
            """);

        var types = passage.Nodes.Select(n => n.Type).ToList();
        Assert.Equal([
            "text", "break", "paragraph_break", "image", "let", "assign", "navigation",
            "popup", "input", "goto", "include_passage", "section", "conditional",
            "switch", "foreach", "checkpoint", "record",
        ], types);
    }

    [Fact]
    public void ConditionalFlat_Deserializes()
    {
        var passage = ParseOne("""
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

        var cond = Assert.IsType<ConditionalNode>(passage.Nodes.Single());
        Assert.Single(cond.Conditions);
        Assert.Equal("round == 1", cond.Conditions[0].If);
        Assert.Null(cond.Else);
        var text = Assert.IsType<TextNode>(cond.Conditions[0].Then.Single());
        Assert.Equal("first round", text.Value);
    }

    [Fact]
    public void ConditionalMultiBranch_Deserializes()
    {
        var passage = ParseOne("""
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
              else:
              - type: 'text'
                value: 'other'
            """);

        var cond = Assert.IsType<ConditionalNode>(passage.Nodes.Single());
        Assert.Equal(2, cond.Conditions.Count);
        Assert.NotNull(cond.Else);
        Assert.Equal("other", Assert.IsType<TextNode>(cond.Else!.Single()).Value);
    }

    [Fact]
    public void SwitchWithDefault_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'switch'
              on: 'players'
              cases:
              - match: 2
                nodes:
                - type: 'text'
                  value: 'two players'
              default:
              - type: 'text'
                value: 'other'
            """);

        var sw = Assert.IsType<SwitchNode>(passage.Nodes.Single());
        Assert.Single(sw.Cases);
        Assert.NotNull(sw.Default);
    }

    [Fact]
    public void SwitchPatternMatch_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'switch'
              on: 'players'
              cases:
              - match: '>3'
                nodes: []
            """);

        var sw = Assert.IsType<SwitchNode>(passage.Nodes.Single());
        Assert.Equal(">3", sw.Cases[0].Match);
    }

    [Fact]
    public void Foreach_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'foreach'
              var: 'entry'
              in: 'players_ranked'
              do:
              - type: 'text'
                value: '{entry.name}'
            """);

        var fe = Assert.IsType<ForEachNode>(passage.Nodes.Single());
        Assert.Equal("entry", fe.Var);
        Assert.Equal("players_ranked", fe.In);
        Assert.Single(fe.Do);
    }

    [Fact]
    public void Section_UsesContentField()
    {
        var passage = ParseOne("""
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

        var section = Assert.IsType<SectionNode>(passage.Nodes.Single());
        Assert.Equal("My Section", section.Title);
        Assert.Single(section.Content);
    }

    [Fact]
    public void Popup_UsesContentField()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              label: 'open'
              state_affecting: true
              content:
              - type: 'text'
                value: 'popup body'
            """);

        var popup = Assert.IsType<PopupNode>(passage.Nodes.Single());
        Assert.Single(popup.Content);
    }

    [Fact]
    public void Navigation_WithOnclick_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'navigation'
              label: 'go'
              target: 'P2'
              state_affecting: true
              onclick:
              - type: 'assign'
                var: 'x'
                expr: '1'
            """);

        var nav = Assert.IsType<NavigationNode>(passage.Nodes.Single());
        Assert.Single(nav.OnClick);
        Assert.IsType<AssignNode>(nav.OnClick[0]);
    }

    [Fact]
    public void DynamicTarget_Preserved()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'navigation'
              label: 'go'
              target: '${nextPsg}'
              state_affecting: true
            """);

        var nav = Assert.IsType<NavigationNode>(passage.Nodes.Single());
        Assert.Contains("${", nav.Target);
    }

    [Fact]
    public void RestextResolution_SubstitutesAllFields()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls:
            [
                """
                format: 'mws/0.3'
                passage_id: 'P1'
                layout: 'narration'
                nodes:
                - type: 'text'
                  value: 'restext://Greeting_001'
                """,
            ],
            restextText: "Greeting_001=Hello there\n");

        var passage = module.Passages.Values.Single();
        var text = Assert.IsType<TextNode>(passage.Nodes.Single());
        Assert.Equal("Hello there", text.Value);
    }

    [Fact]
    public void RestextInExpression_QuotesEscaped()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls:
            [
                """
                format: 'mws/0.3'
                passage_id: 'P1'
                layout: 'narration'
                nodes:
                - type: 'conditional'
                  if: 'notice == "restext://Quoted_001"'
                  then: []
                """,
            ],
            restextText: "Quoted_001=She said \"hello\"\n");

        var passage = module.Passages.Values.Single();
        var cond = Assert.IsType<ConditionalNode>(passage.Nodes.Single());
        Assert.Equal("notice == \"She said \\\"hello\\\"\"", cond.Conditions[0].If);
    }

    [Fact]
    public void VariableManifest_LoadsTypesAndDefaults()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                standard_variables: []
                variables:
                  round:
                    type: 'int'
                    default: 0
                  wolves:
                    type: 'string'
                    default: ''
                """);

        Assert.Equal("int", module.Variables["round"].VarType);
        Assert.Equal(0L, module.Variables["round"].Default);
        Assert.Equal("string", module.Variables["wolves"].VarType);
    }

    [Fact]
    public void VariableManifest_StandardVarsAreNotInTypedDict()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                standard_variables:
                - 'nameA'
                variables:
                  round:
                    type: 'int'
                    default: 0
                """);

        Assert.True(module.Variables["nameA"].IsStandard);
        Assert.False(module.Variables["round"].IsStandard);
    }

    [Fact]
    public void VariableManifest_PlayersIsInt_OthersAreString()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                standard_variables:
                - 'players'
                - 'nameA'
                variables: {}
                """);

        Assert.Equal("int", module.Variables["players"].VarType);
        Assert.Equal("string", module.Variables["nameA"].VarType);
    }

    [Fact]
    public void StartPassageId_FromBeginsHereTag()
    {
        var module = new ModuleLoader().LoadFromSources(
            [
                """
                format: 'mws/0.3'
                passage_id: 'TITLE'
                tags:
                - 'Begins-Here'
                layout: 'narration'
                nodes: []
                """,
                """
                format: 'mws/0.3'
                passage_id: 'OTHER'
                layout: 'narration'
                nodes: []
                """,
            ]);

        Assert.Equal("TITLE", module.StartPassageId);
    }

    [Fact]
    public void Location_Header_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'hub'
            location:
              name: 'The Hospital'
              icon: 'icon://hospital_icon'
            nodes: []
            """);

        Assert.NotNull(passage.Location);
        Assert.Equal("The Hospital", passage.Location!.Name);
        Assert.Equal("icon://hospital_icon", passage.Location.Icon);
    }

    [Fact]
    public void CheckProgress_Header_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'hub'
            check_progress: 'Hospital0'
            nodes: []
            """);

        Assert.Equal("Hospital0", passage.CheckProgress);
    }

    [Fact]
    public void Ending_Header_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'END-1'
            layout: 'narration'
            ending: true
            nodes: []
            """);

        Assert.True(passage.Ending);
    }

    [Fact]
    public void Ending_Header_DefaultsToFalse()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes: []
            """);

        Assert.False(passage.Ending);
    }
}
