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
    public void TextNode_NonEmphasisStyle_EmitsStyleField()
    {
        // Regression: TextNode.ToDict() used to consume Style only to decide bold/italic markdown
        // wrapping (via ApplyInlineStyle) and never wrote a `style:` key at all — silently dropping
        // any non-emphasis style, e.g. the special-event overlay marker (see
        // PassageBodyVisitor.IsShowEventPopupCall), which needs `style:` in the v0.3 output for
        // module CSS to hook into.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes = [new Masterwork.Extractor.TextNode { Template = "Special Event", Style = "special-event" }],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("Special Event", node["value"]);
        Assert.Equal("special-event", node["style"]);
    }

    [Fact]
    public void TextNode_BoldStyle_OmitsStyleField()
    {
        // "bold"/"italic" stay baked into the markdown value (**...**) — no separate style: field,
        // matching how hand-authored content expresses emphasis inline.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes = [new Masterwork.Extractor.TextNode { Template = "Heading", Style = "bold" }],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("**Heading**", node["value"]);
        Assert.False(node.ContainsKey("style"));
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
    public void EndOfGeneration_EmitsConfirmOkayButton()
    {
        // Regression: this popup auto-displays (no label) with no target/onclose of its own — without
        // an explicit okay button, RenderedPopupView renders no footer at all and the popup could
        // never be dismissed. The reference app's own button reads "CONFIRM" (Main.unity GameObject
        // 2047491225, ViewEndOfGeneration's Accept button).
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes = [new EndOfGenerationNode { Generation = 1, Message = "Generation One has ended." }],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("popup", node["type"]);
        Assert.Equal("end_of_generation", node["layout"]);
        Assert.Equal("Confirm", node["okay"]);
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
        Assert.Equal("input-popup", popup["layout"]);
        Assert.Equal("Feverheart", popup["target"]);
        Assert.Equal("Continue", popup["okay"]);
        Assert.Equal(true, popup["snapshot"]);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.Equal(2, content.Count);
        Assert.Equal("text", content[0]["type"]);
        Assert.Equal("How do you feel?", content[0]["value"]);
        Assert.Equal("input", content[1]["type"]);
        // input.PromptId ("Feverheart") is just the OnGenerationBtn call's resume-passage
        // argument, not a real label — the input must not display it (or anything else).
        Assert.Equal("", content[1]["label"]);
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
        // A single if/else pair flattens to if/then/else directly — no `conditions:` wrapper (that's
        // only for an if/elseif/.../else chain).
        var headerThen = (List<Dictionary<string, object?>>)headerCond["then"]!;
        var headerImage = Assert.Single(headerThen);
        Assert.Equal("image", headerImage["type"]);
        Assert.Equal("image://setup/Creepy_Icon", headerImage["asset"]);
        var headerElse = (List<Dictionary<string, object?>>)headerCond["else"]!;
        Assert.Single(headerElse);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        var contentCond = Assert.Single(content, n => (string)n["type"]! == "conditional");
        var contentThen = (List<Dictionary<string, object?>>)contentCond["then"]!;
        Assert.Equal(2, contentThen.Count); // break + text, image removed
        Assert.DoesNotContain(contentThen, n => (string)n["type"]! == "image");
    }

    [Fact]
    public void SetupNotificationBlock_ConditionalWithOnlyImageAndBreak_CollapsesAndTrimsLeadingBreak()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00003-Hospital1.mws.yaml and
        // 00004-Devastation1.mws.yaml — each branch is exactly [image, break] (no other
        // branch-specific content). Once the image is hoisted to the header, every branch's
        // remaining content is nothing but its own trailing break — the exact same break regardless
        // of which branch matches — so the leftover content conditional collapses to one plain break
        // (CollapseIfBreakOnly). Here nothing precedes it in the popup's own content, so that
        // collapsed break is *also* leading and TrimEdgeBreaks removes it entirely — it must not
        // survive as a stray break at the very start of content.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode(),
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
                                    Condition = "seedy == \"yes\"",
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/AngryMobSetup2", Style = "setup-image" },
                                        new Masterwork.Extractor.BreakNode(),
                                    ],
                                },
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Else = true,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/AngryMobSetup1", Style = "setup-image" },
                                        new Masterwork.Extractor.BreakNode(),
                                    ],
                                },
                            ],
                        },
                        new Masterwork.Extractor.TextNode { Template = "Turn to page." },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.DoesNotContain(content, n => (string)n["type"]! == "conditional");
        Assert.Single(content);
        Assert.Equal("text", content[0]["type"]);
    }

    [Fact]
    public void SplitPopupHeaderNodes_TrailingBreakAfterLastRealContent_IsTrimmed()
    {
        // Symmetric case: a trailing break left dangling at the very end of `content` (nothing after
        // it — SplitPopupHeaderNodes builds this list independently of whatever, if anything, the
        // caller appends afterward) must be trimmed too, not just a leading one.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode(),
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.TextNode { Template = "Turn to page." },
                        new Masterwork.Extractor.BreakNode(),
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.Single(content);
        Assert.Equal("text", content[0]["type"]);
    }

    [Fact]
    public void SetupNotificationBlock_CollapsedBreakAdjacentToFollowingLiteralBreak_MergesToParagraphBreak()
    {
        // Regression: Masterwork-Modules/cost-of-disease/passages/00003-Hospital1.mws.yaml's actual
        // full shape — the break-only conditional collapses to a single break (previous test), and
        // Cradle's own next statement is a second, literal lineBreak() right after the if/else block.
        // BreakFilter already merges consecutive breaks, but only for pairs it can see during its own
        // pass — this collapsed break didn't exist yet then (SplitPopupHeaderNodes synthesizes it
        // afterward), so without this merge the two would sit side by side as two plain breaks
        // instead of the one paragraph break the source's two real lineBreak() calls call for. A
        // leading text node is placed inside SetupBlockNode.Nodes itself (unlike the previous test)
        // so the merged break isn't *also* leading from SplitPopupHeaderNodes' own point of view —
        // note that SetupNotificationNode.Title alone wouldn't do this: it's prepended by the caller
        // (TransformSetupNotificationBlock) entirely outside SplitPopupHeaderNodes' own view of
        // `content`, so TrimEdgeBreaks would still see (and remove) a leading break regardless.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode(),
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.TextNode { Template = "Preamble." },
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "seedy == \"yes\"",
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/AngryMobSetup2", Style = "setup-image" },
                                        new Masterwork.Extractor.BreakNode(),
                                    ],
                                },
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Else = true,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/AngryMobSetup1", Style = "setup-image" },
                                        new Masterwork.Extractor.BreakNode(),
                                    ],
                                },
                            ],
                        },
                        new Masterwork.Extractor.BreakNode(),
                        new Masterwork.Extractor.TextNode { Template = "Turn to page." },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var popup = Nodes0(d);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.Equal("text", content[0]["type"]); // "Preamble."
        Assert.Equal("break", content[1]["type"]);
        Assert.Equal("paragraph", content[1]["style"]);
        Assert.Equal("text", content[2]["type"]); // "Turn to page."
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
        // A single if/else pair flattens to if/then/else directly — no `conditions:` wrapper.
        var headerThen = (List<Dictionary<string, object?>>)headerCond["then"]!;
        Assert.Single(headerThen); // just the image
        var headerElse = (List<Dictionary<string, object?>>)headerCond["else"]!;
        Assert.Empty(headerElse); // non-qualifying branch contributes nothing to header

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        var contentCond = Assert.Single(content, n => (string)n["type"]! == "conditional");
        var contentThen = (List<Dictionary<string, object?>>)contentCond["then"]!;
        Assert.Single(contentThen); // text only, image removed
        Assert.DoesNotContain(contentThen, n => (string)n["type"]! == "image");
        var contentElse = (List<Dictionary<string, object?>>)contentCond["else"]!;
        Assert.Single(contentElse); // unchanged — no leading image to remove
    }

    [Fact]
    public void Conditional_SingleIfWithElse_FlattensToIfThenElse()
    {
        // `conditions:` is only needed for an if/elseif/.../else chain (2+ if-branches) — a plain
        // if/else pair should read the condition and both bodies directly on the node, matching
        // docs/mws-format-latest.md's documented flat form (already parsed either way — see
        // PassageYamlParser.BuildConditional).
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ConditionalNode
                {
                    Branches =
                    [
                        new Masterwork.Extractor.ConditionalBranch { Condition = "nameA == \"\"", Nodes = [new Masterwork.Extractor.TextNode { Template = "Enter your name." }] },
                        new Masterwork.Extractor.ConditionalBranch { Else = true, Nodes = [new Masterwork.Extractor.TextNode { Template = "Welcome back." }] },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var cond = Nodes0(d);

        Assert.Equal("conditional", cond["type"]);
        Assert.DoesNotContain("conditions", cond.Keys);
        Assert.Equal("nameA == \"\"", cond["if"]);
        Assert.Equal("Enter your name.", ((List<Dictionary<string, object?>>)cond["then"]!)[0]["value"]);
        Assert.Equal("Welcome back.", ((List<Dictionary<string, object?>>)cond["else"]!)[0]["value"]);
    }

    [Fact]
    public void Conditional_IfElseIfElse_StillUsesConditionsWrapper()
    {
        // Contrast with the previous test: 2+ if-branches (a real elseif chain) still need the
        // `conditions:` list — flattening only applies to a single if-branch.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ConditionalNode
                {
                    Branches =
                    [
                        new Masterwork.Extractor.ConditionalBranch { Condition = "players >= 3", Nodes = [new Masterwork.Extractor.TextNode { Template = "Three or more." }] },
                        new Masterwork.Extractor.ConditionalBranch { Condition = "players == 2", Nodes = [new Masterwork.Extractor.TextNode { Template = "Two." }] },
                        new Masterwork.Extractor.ConditionalBranch { Else = true, Nodes = [new Masterwork.Extractor.TextNode { Template = "None." }] },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var cond = Nodes0(d);

        Assert.Equal("conditional", cond["type"]);
        Assert.DoesNotContain("if", cond.Keys);
        Assert.DoesNotContain("then", cond.Keys);
        var conditions = (List<Dictionary<string, object?>>)cond["conditions"]!;
        Assert.Equal(2, conditions.Count);
        Assert.NotNull(cond["else"]);
    }
}
