using Masterwork.Extractor;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

// Targets V2Serializer's output shape directly (v0.3 dict → v0.4 field/discriminator changes), per
// CLAUDE.md's test-strategy note: ExtractorTests.cs asserts against the extractor-internal MwsNode
// types (stable across format revisions); this file asserts against the v0.3/v0.4 *output* concern.
public class V2SerializerTests
{
    private static Dictionary<string, object?> Nodes0(Dictionary<string, object?> passageDict) =>
        (Dictionary<string, object?>)((List<Dictionary<string, object?>>)passageDict["nodes"]!)[0];

    [Fact]
    public void Format_IsMws04()
    {
        var passage = new MwsPassage { PassageId = "P1" };
        var d = V2Serializer.ToDict(passage);
        Assert.Equal("mws/0.4", d["format"]);
    }

    [Fact]
    public void Link_EmitsTypeLink()
    {
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes = [new Masterwork.Extractor.LinkNode { Label = "Go", Target = "P2", StateAffecting = true }],
        };

        var d = V2Serializer.ToDict(passage);

        Assert.Equal("link", Nodes0(d)["type"]);
    }

    [Fact]
    public void ParagraphBreak_EmitsBreakWithParagraphStyle()
    {
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes = [new ParagraphBreakNode()],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("break", node["type"]);
        Assert.Equal("paragraph", node["style"]);
    }

    [Fact]
    public void InputPrompt_EmitsGuardedAutoPopupConditional()
    {
        var passage = new MwsPassage
        {
            PassageId = "Feverheart",
            Nodes =
            [
                new InputPromptNode
                {
                    PromptId = "Feverheart",
                    Text = "How do you feel?",
                    InputType = "number",
                    StoreIn = "feverheart",
                    ResumePassage = "Feverheart",
                },
            ],
        };
        var vars = new Dictionary<string, VarDef>();
        var ctx = new SerializationContext(SourceRelativePath: null, PassageFileMap: null, Variables: vars);

        var d = V2Serializer.ToDict(passage, ctx);
        var cond = Nodes0(d);

        Assert.Equal("conditional", cond["type"]);
        Assert.Equal("!feverheart_submitted", cond["if"]);

        var then = (List<Dictionary<string, object?>>)cond["then"]!;
        var popup = Assert.Single(then);
        Assert.Equal("popup", popup["type"]);
        Assert.Equal("Feverheart", popup["target"]);
        Assert.Equal("Continue", popup["okay"]);
        Assert.Equal(true, popup["snapshot"]);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.Equal(2, content.Count);
        Assert.Equal("text", content[0]["type"]);
        Assert.Equal("How do you feel?", content[0]["value"]);
        Assert.Equal("input", content[1]["type"]);
        Assert.Equal("feverheart", content[1]["var"]);
        Assert.DoesNotContain("text", content[1].Keys);
        Assert.DoesNotContain("input", content[1].Keys);

        var onclose = (List<Dictionary<string, object?>>)popup["onclose"]!;
        var assign = Assert.Single(onclose);
        Assert.Equal("assign", assign["type"]);
        Assert.Equal("feverheart_submitted", assign["var"]);
        Assert.Equal("true", assign["expr"]);
    }

    [Fact]
    public void InputPrompt_RegistersSyntheticGuardVariable()
    {
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes = [new InputPromptNode { PromptId = "P1", Text = "?", StoreIn = "score", ResumePassage = "P1" }],
        };
        var vars = new Dictionary<string, VarDef>();
        var ctx = new SerializationContext(SourceRelativePath: null, PassageFileMap: null, Variables: vars);

        V2Serializer.ToDict(passage, ctx);

        Assert.True(vars.TryGetValue("score_submitted", out var guard));
        Assert.Equal(VarKind.Boolean, guard!.VarType);
    }
}
