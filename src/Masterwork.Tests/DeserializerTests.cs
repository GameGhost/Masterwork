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
            - type: 'image'
              asset: 'icon://foo'
            - type: 'let'
              var: 'x'
              expr: '1'
            - type: 'assign'
              var: 'y'
              expr: '2'
            - type: 'link'
              label: 'go'
              target: 'P2'
              snapshot: true
            - type: 'popup'
              label: 'open'
              content: []
              snapshot: true
            - type: 'input'
              label: 'ask'
              var: 'z'
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
            "text", "break", "image", "let", "assign", "link",
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
    public void ConditionalFlatWithElse_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'conditional'
              if: 'nameA == ""'
              then:
              - type: 'text'
                value: 'enter name'
              else:
              - type: 'text'
                value: 'welcome back'
            """);

        var cond = Assert.IsType<ConditionalNode>(passage.Nodes.Single());
        Assert.Single(cond.Conditions);
        Assert.Equal("nameA == \"\"", cond.Conditions[0].If);
        Assert.Equal("enter name", Assert.IsType<TextNode>(cond.Conditions[0].Then.Single()).Value);
        Assert.NotNull(cond.Else);
        Assert.Equal("welcome back", Assert.IsType<TextNode>(cond.Else!.Single()).Value);
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
              snapshot: true
              content:
              - type: 'text'
                value: 'popup body'
            """);

        var popup = Assert.IsType<PopupNode>(passage.Nodes.Single());
        Assert.Single(popup.Content);
    }

    [Fact]
    public void Popup_UsesHeaderField()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              header:
              - type: 'image'
                asset: 'image://setup/StorybookToken'
                style: 'setup-image'
              content:
              - type: 'text'
                value: 'popup body'
            """);

        var popup = Assert.IsType<PopupNode>(passage.Nodes.Single());
        var header = Assert.Single(popup.Header);
        var image = Assert.IsType<ImageNode>(header);
        Assert.Equal("image://setup/StorybookToken", image.Asset);
        Assert.Equal("setup-image", image.Style);
        Assert.Single(popup.Content);
    }

    [Fact]
    public void Popup_NoHeaderField_DefaultsToEmpty()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              content:
              - type: 'text'
                value: 'popup body'
            """);

        var popup = Assert.IsType<PopupNode>(passage.Nodes.Single());
        Assert.Empty(popup.Header);
    }

    [Fact]
    public void Navigation_WithOnclick_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'go'
              target: 'P2'
              snapshot: true
              onclick:
              - type: 'assign'
                var: 'x'
                expr: '1'
            """);

        var nav = Assert.IsType<LinkNode>(passage.Nodes.Single());
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
            - type: 'link'
              label: 'go'
              target: '${nextPsg}'
              snapshot: true
            """);

        var nav = Assert.IsType<LinkNode>(passage.Nodes.Single());
        Assert.Contains("${", nav.Target);
    }

    [Fact]
    public void LinkSnapshot_Absent_DefaultsToFalseWithNoLabel()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'go'
              target: 'P2'
            """);

        var link = Assert.IsType<LinkNode>(passage.Nodes.Single());
        Assert.False(link.StateAffecting);
        Assert.Null(link.SnapshotLabel);
    }

    [Fact]
    public void LinkSnapshot_Bool_ParsesAsStateAffectingWithNoLabel()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'go'
              target: 'P2'
              snapshot: true
            """);

        var link = Assert.IsType<LinkNode>(passage.Nodes.Single());
        Assert.True(link.StateAffecting);
        Assert.Null(link.SnapshotLabel);
    }

    [Fact]
    public void LinkSnapshot_StringValue_ImpliesStateAffectingAndSetsLabel()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'go'
              target: 'P2'
              snapshot: 'You chose to lie'
            """);

        var link = Assert.IsType<LinkNode>(passage.Nodes.Single());
        Assert.True(link.StateAffecting);
        Assert.Equal("You chose to lie", link.SnapshotLabel);
    }

    [Fact]
    public void PopupSnapshot_StringValue_ImpliesStateAffectingAndSetsLabel()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'popup'
              okay: 'Continue'
              target: 'P2'
              snapshot: 'popup label'
              content: []
            """);

        var popup = Assert.IsType<PopupNode>(passage.Nodes.Single());
        Assert.True(popup.StateAffecting);
        Assert.Equal("popup label", popup.SnapshotLabel);
    }

    [Fact]
    public void GotoSnapshotLabel_Deserializes()
    {
        var passage = ParseOne("""
            format: 'mws/0.4'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'goto'
              target: 'P2'
              snapshot_label: 'goto label'
            """);

        var go = Assert.IsType<GotoNode>(passage.Nodes.Single());
        Assert.Equal("goto label", go.SnapshotLabel);
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

        Assert.Equal(VarKind.Integer, module.Variables["round"].VarType);
        Assert.Equal(0L, module.Variables["round"].Default);
        Assert.Equal(VarKind.String, module.Variables["wolves"].VarType);
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

        Assert.Equal(VarKind.Integer, module.Variables["players"].VarType);
        Assert.Equal(VarKind.String, module.Variables["nameA"].VarType);
    }

    [Fact]
    public void VariableManifest_OneLineForm_ParsesTypeWithNoExplicitDefault()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                variables:
                  round: 'int'
                  wolves: 'string'
                  seen: 'bool'
                  entry: 'record'
                  tags: 'string_array'
                """);

        Assert.Equal(VarKind.Integer, module.Variables["round"].VarType);
        Assert.Null(module.Variables["round"].Default);
        Assert.Equal(VarKind.Boolean, module.Variables["seen"].VarType);
        Assert.Equal(VarKind.Record, module.Variables["entry"].VarType);
        Assert.Equal(VarKind.StringArray, module.Variables["tags"].VarType);
    }

    [Fact]
    public void VariableManifest_OneLineAndExpandedForms_CanBeMixed()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                variables:
                  round: 'int'
                  final5:
                    type: 'int'
                    default: 3
                """);

        Assert.Null(module.Variables["round"].Default);
        Assert.Equal(3L, module.Variables["final5"].Default);
    }

    [Fact]
    public void VariableManifest_UnrecognizedType_WarnsAndSkipsVariable()
    {
        var module = new ModuleLoader().LoadFromSources(
            passageYamls: [],
            variablesYaml: """
                variables:
                  bogus: 'not_a_real_type'
                """);

        Assert.False(module.Variables.ContainsKey("bogus"));
        Assert.Contains(module.Warnings.Items, w => w.Kind == "wrong_field_type");
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
