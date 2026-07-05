using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

// Covers the deserializer's error/warning policy: missing vs. malformed vs. unrecognized fields.
// See PassageYamlParser / YamlNodeExtensions doc comments for the policy this pins down.
public class DeserializerWarningsTests
{
    private static (MwsPassageDoc passage, ModuleWarnings warnings) LoadOne(
        string yaml, string? variablesYaml = null)
    {
        var module = new ModuleLoader().LoadFromSources([yaml], variablesYaml);
        return (module.Passages.Values.Single(), module.Warnings);
    }

    // ── Missing required fields ─────────────────────────────────────────────

    [Fact]
    public void MissingPassageId_Throws()
    {
        var ex = Assert.Throws<MwsParseException>(() => new ModuleLoader().LoadFromSources([
            """
            format: 'mws/0.3'
            layout: 'narration'
            nodes: []
            """,
        ]));
        Assert.Contains("passage_id", ex.Message);
    }

    [Fact]
    public void MissingLayout_Throws()
    {
        var ex = Assert.Throws<MwsParseException>(() => new ModuleLoader().LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            nodes: []
            """,
        ]));
        Assert.Contains("layout", ex.Message);
    }

    [Fact]
    public void MissingNodeType_Throws()
    {
        var ex = Assert.Throws<MwsParseException>(() => new ModuleLoader().LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - value: 'hi'
            """,
        ]));
        Assert.Contains("type", ex.Message);
    }

    [Fact]
    public void MissingRequiredNodeField_Throws()
    {
        var ex = Assert.Throws<MwsParseException>(() => new ModuleLoader().LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
            """,
        ]));
        Assert.Contains("value", ex.Message);
    }

    // ── Wrong-shaped required field ──────────────────────────────────────────

    [Fact]
    public void RequiredField_WrongShape_ThrowsWithClearMessage()
    {
        var ex = Assert.Throws<MwsParseException>(() => new ModuleLoader().LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value:
                nested: 'oops'
            """,
        ]));
        Assert.Contains("value", ex.Message);
        Assert.Contains("mapping", ex.Message);
    }

    // ── Wrong-shaped optional field: warning + fallback ──────────────────────

    [Fact]
    public void WrongTypeOptionalStringField_LogsWarningAndFallsBackToNull()
    {
        var (passage, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            title:
              nested: 'oops'
            nodes: []
            """);

        Assert.Null(passage.Title);
        Assert.Contains(warnings.Items, w => w.Kind == "wrong_field_type" && w.Message.Contains("title"));
    }

    [Fact]
    public void WrongTypeTagsField_LogsWarningAndFallsBackToEmpty()
    {
        var (passage, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            tags: 'HUB'
            nodes: []
            """);

        Assert.Empty(passage.Tags);
        Assert.Contains(warnings.Items, w => w.Kind == "wrong_field_type" && w.Message.Contains("tags"));
    }

    [Fact]
    public void NonScalarListElement_LogsWarningAndSkipsElement()
    {
        var (passage, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            tags:
            - 'HUB'
            - {nested: 'oops'}
            nodes: []
            """);

        Assert.Equal(["HUB"], passage.Tags);
        Assert.Contains(warnings.Items, w => w.Kind == "wrong_field_type" && w.Message.Contains("tags"));
    }

    [Fact]
    public void NonMappingNodeListElement_LogsWarningAndSkipsElement()
    {
        var (passage, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'hi'
            - 'just a string'
            """);

        var text = Assert.IsType<TextNode>(Assert.Single(passage.Nodes));
        Assert.Equal("hi", text.Value);
        Assert.Contains(warnings.Items, w => w.Kind == "wrong_field_type" && w.Message.Contains("node mapping"));
    }

    // ── Unmatched / unknown fields ───────────────────────────────────────────

    [Fact]
    public void UnmatchedPassageHeaderField_LogsWarning()
    {
        var (_, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            extra_field: 'oops'
            nodes: []
            """);

        Assert.Contains(warnings.Items, w => w.Kind == "unmatched_field" && w.Message.Contains("extra_field"));
    }

    [Fact]
    public void UnmatchedNodeField_LogsWarning()
    {
        var (_, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'hi'
              extra_field: 'oops'
            """);

        Assert.Contains(warnings.Items, w => w.Kind == "unmatched_field" && w.Message.Contains("extra_field"));
    }

    [Fact]
    public void UnmatchedLocationField_LogsWarning()
    {
        var (_, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'hub'
            location:
              name: 'The Hospital'
              extra_field: 'oops'
            nodes: []
            """);

        Assert.Contains(warnings.Items, w => w.Kind == "unmatched_field" && w.Message.Contains("extra_field"));
    }

    [Fact]
    public void UnknownNodeType_LogsWarningAndProducesUnknownNode()
    {
        var (passage, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'mystery_node'
              foo: 'bar'
            """);

        var node = Assert.IsType<UnknownNode>(Assert.Single(passage.Nodes));
        Assert.Equal("mystery_node", node.Type);
        Assert.Contains(warnings.Items, w => w.Kind == "unknown_node_type" && w.Message.Contains("mystery_node"));
    }

    // ── Malformed bool: friendly error ───────────────────────────────────────

    [Fact]
    public void MalformedBoolField_ThrowsFriendlyError()
    {
        var ex = Assert.Throws<MwsParseException>(() => new ModuleLoader().LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'navigation'
              label: 'Go'
              target: 'P2'
              state_affecting: 'yes'
            """,
        ]));
        Assert.Contains("state_affecting", ex.Message);
        Assert.Contains("yes", ex.Message);
    }

    // ── Format version drift ─────────────────────────────────────────────────

    [Fact]
    public void UnexpectedFormatVersion_LogsWarning()
    {
        var (_, warnings) = LoadOne("""
            format: 'mws/0.2'
            passage_id: 'P1'
            layout: 'narration'
            nodes: []
            """);

        Assert.Contains(warnings.Items, w => w.Kind == "unexpected_format_version" && w.Message.Contains("mws/0.2"));
    }

    // ── Alignment enum ───────────────────────────────────────────────────────

    [Fact]
    public void ValidAlign_ParsesToEnum()
    {
        var (passage, _) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'hi'
              align: 'center'
            """);

        var text = Assert.IsType<TextNode>(Assert.Single(passage.Nodes));
        Assert.Equal(Alignment.Center, text.Align);
    }

    [Fact]
    public void MissingAlign_IsNull()
    {
        var (passage, _) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'hi'
            """);

        var text = Assert.IsType<TextNode>(Assert.Single(passage.Nodes));
        Assert.Null(text.Align);
    }

    [Fact]
    public void InvalidAlign_LogsWarningAndFallsBackToNull()
    {
        var (passage, warnings) = LoadOne("""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'hi'
              align: 'centre'
            """);

        var text = Assert.IsType<TextNode>(Assert.Single(passage.Nodes));
        Assert.Null(text.Align);
        Assert.Contains(warnings.Items, w => w.Kind == "invalid_enum_value" && w.Message.Contains("centre"));
    }

    // ── InputValueType enum ──────────────────────────────────────────────────

    [Theory]
    [InlineData("string", InputValueType.String)]
    [InlineData("number", InputValueType.Number)]
    public void ValidInputType_ParsesToEnum(string raw, InputValueType expected)
    {
        var (passage, _) = LoadOne($$"""
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'input'
              label: 'Enter'
              text: 'enter a value'
              input: '{{raw}}'
              var: 'x'
              onsubmit: 'P2'
            """);

        var input = Assert.IsType<InputNode>(Assert.Single(passage.Nodes));
        Assert.Equal(expected, input.InputType);
    }

    [Fact]
    public void InvalidInputType_Throws()
    {
        var ex = Assert.Throws<MwsParseException>(() => new ModuleLoader().LoadFromSources([
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            layout: 'narration'
            nodes:
            - type: 'input'
              label: 'Enter'
              text: 'enter a value'
              input: 'boolean'
              var: 'x'
              onsubmit: 'P2'
            """,
        ]));
        Assert.Contains("input", ex.Message);
        Assert.Contains("boolean", ex.Message);
    }

    // ── VariableManifest ─────────────────────────────────────────────────────

    [Fact]
    public void UnmatchedVariablesYamlField_LogsWarning()
    {
        var module = new ModuleLoader().LoadFromSources([], variablesYaml: """
            standard_variables: []
            variables: {}
            extra_top_level: 'oops'
            """);

        Assert.Contains(module.Warnings.Items, w => w.Kind == "unmatched_field" && w.Message.Contains("extra_top_level"));
    }

    [Fact]
    public void WrongShapedVariableDef_LogsWarningAndSkips()
    {
        var module = new ModuleLoader().LoadFromSources([], variablesYaml: """
            standard_variables: []
            variables:
              round: 'not a mapping'
            """);

        Assert.DoesNotContain("round", module.Variables.Keys);
        Assert.Contains(module.Warnings.Items, w => w.Kind == "wrong_field_type" && w.Message.Contains("round"));
    }

    [Fact]
    public void UnmatchedVariableDefField_LogsWarning()
    {
        var module = new ModuleLoader().LoadFromSources([], variablesYaml: """
            standard_variables: []
            variables:
              round:
                type: 'int'
                default: 0
                extra_field: 'oops'
            """);

        Assert.Contains(module.Warnings.Items, w => w.Kind == "unmatched_field" && w.Message.Contains("extra_field"));
    }
}
