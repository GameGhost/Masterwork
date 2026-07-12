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
    public void Subtitle_EmitsSubtitleField()
    {
        var passage = new MwsPassage { PassageId = "P1", Title = "The Title", Subtitle = "The Subtitle" };

        var d = V2Serializer.ToDict(passage);

        Assert.Equal("The Title", d["title"]);
        Assert.Equal("The Subtitle", d["subtitle"]);
    }

    [Fact]
    public void NoSubtitle_OmitsSubtitleField()
    {
        var passage = new MwsPassage { PassageId = "P1" };

        var d = V2Serializer.ToDict(passage);

        Assert.False(d.ContainsKey("subtitle"));
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

    [Fact]
    public void SetupNotificationBlock_ImageWithSetupImageStyle_RoutesToHeaderNotContent()
    {
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode { Title = "Setup Title" },
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/StorybookToken", Style = "setup-image" },
                        new Masterwork.Extractor.TextNode { Template = "Body text" },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        Assert.Equal("popup", popup["type"]);
        Assert.Equal("setup", popup["layout"]);

        var header = (List<Dictionary<string, object?>>)popup["header"]!;
        var headerImage = Assert.Single(header);
        Assert.Equal("image", headerImage["type"]);
        Assert.Equal("image://setup/StorybookToken", headerImage["asset"]);
        Assert.Equal("setup-image", headerImage["style"]);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.DoesNotContain(content, n => (string)n["type"]! == "image");
    }

    [Fact]
    public void SetupNotificationBlock_NoSetupImageStyle_NoHeaderField()
    {
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode { Title = "Setup Title" },
                new SetupBlockNode
                {
                    Nodes = [new Masterwork.Extractor.TextNode { Template = "Body text" }],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        Assert.DoesNotContain("header", popup.Keys);
    }

    [Fact]
    public void SetupNotificationBlock_ConditionalSetupImage_RoutesWholeConditionalToHeader()
    {
        // The dynamic/ternary shape TryProcessSetupImageAssignment produces (PassageBodyVisitor) —
        // a ConditionalNode whose every branch is exactly one setup-image ImageNode — must also
        // route to header as a whole, not just a bare top-level ImageNode.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode { Title = "Setup Title" },
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "society == \"Fraternity of Hunters\"",
                                    Nodes = [new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/S1_HunterToken", Style = "setup-image" }],
                                },
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Else = true,
                                    Nodes = [new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/S1_WolfToken", Style = "setup-image" }],
                                },
                            ],
                        },
                        new Masterwork.Extractor.TextNode { Template = "Body text" },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        var header = (List<Dictionary<string, object?>>)popup["header"]!;
        var headerCond = Assert.Single(header);
        Assert.Equal("conditional", headerCond["type"]);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.DoesNotContain(content, n => (string)n["type"]! == "conditional");
    }

    [Fact]
    public void SetupNotificationBlock_ConditionalWithLeadingImageAndMoreContent_SplitsIntoParallelConditionals()
    {
        // The far more common real shape than a pure ternary: each branch sets _SetupImage first,
        // then has its own branch-specific body content (e.g. Trigger35Points' if/else, where each
        // branch is [image, break, text]). The image is hoisted into a header conditional; the rest
        // of each branch stays in a content conditional sharing the same conditions.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode { Title = "Setup Title" },
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "thirtyFivevp == \"creep\"",
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/Creepy_Icon", Style = "setup-image" },
                                        new Masterwork.Extractor.BreakNode(),
                                        new Masterwork.Extractor.TextNode { Template = "gain 2" },
                                    ],
                                },
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Else = true,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/DiscardEstateUpgrade_Icon", Style = "setup-image" },
                                        new Masterwork.Extractor.BreakNode(),
                                        new Masterwork.Extractor.TextNode { Template = "lose 4VP" },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        var header = (List<Dictionary<string, object?>>)popup["header"]!;
        var headerCond = Assert.Single(header);
        var headerConditions = (List<Dictionary<string, object?>>)headerCond["conditions"]!;
        var headerThen = (List<Dictionary<string, object?>>)Assert.Single(headerConditions)["then"]!;
        var headerImage = Assert.Single(headerThen);
        Assert.Equal("image", headerImage["type"]);
        Assert.Equal("image://setup/Creepy_Icon", headerImage["asset"]);
        var headerElse = (List<Dictionary<string, object?>>)headerCond["else"]!;
        Assert.Single(headerElse);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        var contentCond = Assert.Single(content, n => (string)n["type"]! == "conditional");
        var contentConditions = (List<Dictionary<string, object?>>)contentCond["conditions"]!;
        var contentThen = (List<Dictionary<string, object?>>)Assert.Single(contentConditions)["then"]!;
        Assert.Equal(2, contentThen.Count); // break + text, image removed
        Assert.DoesNotContain(contentThen, n => (string)n["type"]! == "image");
    }

    [Fact]
    public void SetupNotificationBlock_ConditionalWithOnlySomeBranchesStartingWithImage_SplitsPartially()
    {
        // Real shape from Gen1Creepy-ConcealExpose: one branch starts with a setup-image + more
        // content, the other branch is something else entirely (here, unrelated text) with no
        // leading image at all. The header conditional's non-qualifying branch is just empty
        // (nothing to show); the content conditional keeps that branch completely unchanged.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode { Title = "Setup Title" },
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "wolves == \"evil\"",
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/DiscardEstateUpgrade_Icon", Style = "setup-image" },
                                        new Masterwork.Extractor.TextNode { Template = "Discard an upgrade" },
                                    ],
                                },
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Else = true,
                                    Nodes = [new Masterwork.Extractor.TextNode { Template = "unrelated fallback text" }],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        var header = (List<Dictionary<string, object?>>)popup["header"]!;
        var headerCond = Assert.Single(header);
        var headerConditions = (List<Dictionary<string, object?>>)headerCond["conditions"]!;
        var headerThen = (List<Dictionary<string, object?>>)Assert.Single(headerConditions)["then"]!;
        Assert.Single(headerThen); // just the image
        var headerElse = (List<Dictionary<string, object?>>)headerCond["else"]!;
        Assert.Empty(headerElse); // non-qualifying branch contributes nothing to header

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        var contentCond = Assert.Single(content, n => (string)n["type"]! == "conditional");
        var contentConditions = (List<Dictionary<string, object?>>)contentCond["conditions"]!;
        var contentThen = (List<Dictionary<string, object?>>)Assert.Single(contentConditions)["then"]!;
        Assert.Single(contentThen); // text only, image removed
        Assert.DoesNotContain(contentThen, n => (string)n["type"]! == "image");
        var contentElse = (List<Dictionary<string, object?>>)contentCond["else"]!;
        Assert.Single(contentElse); // unchanged — no leading image to remove
    }
}
