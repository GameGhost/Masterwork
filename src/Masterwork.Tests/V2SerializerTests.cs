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
    public void SectionHeadingAndBody_EmitsHubCardStyle()
    {
        // Regression: TransformSection's style parameter existed specifically for this pairing but
        // was never passed at either call site, so every hub passage's location cards silently fell
        // back to style-default — my-fathers-work-template's .mws-section.style-hub-card border never
        // matched anything a real extracted module actually emitted.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.SectionHeadingNode { Text = "Attracting Attention" },
                new Masterwork.Extractor.SectionBodyNode { Nodes = [new Masterwork.Extractor.TextNode { Template = "Body" }] },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("section", node["type"]);
        Assert.Equal("hub-card", node["style"]);
        Assert.Equal("Attracting Attention", node["title"]);
    }

    [Fact]
    public void LetRandomValues_MixedTemplateChoice_IsQuoted()
    {
        // Regression: StringValueToExpr's fallthrough returned a mixed literal-text-plus-{var}
        // string unquoted, producing invalid expr syntax once the shuffled-array literal reached
        // an actual '{' character — "Unexpected trailing input: '{'" at load time. Real-world
        // crash: Fear of the Unknown's JunkPalaceSignIn passage, whose either() call mixes a
        // concatenation-built choice ("...center of " + townname + " and met with " + warden)
        // with two plain-literal choices. The mixed choice must stay a quoted string (braces
        // survive as literal characters, interpolated later when the temp var is displayed via
        // {tempVar} in a text node) — same conclusion as the other two choices, just also
        // containing embedded {var} placeholders.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.LetNode
                {
                    Var = "_rnd_0",
                    Random = new Masterwork.Extractor.VarRandom
                    {
                        RandomType = "choose-one",
                        SeedKey = "P1_0",
                        Values = ["center of {townname} and met with {warden}.", "plain choice"],
                    },
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var expr = (string)Nodes0(d)["expr"]!;

        Assert.Contains("\"center of {townname} and met with {warden}.\"", expr);
        Assert.Contains("\"plain choice\"", expr);
    }

    [Fact]
    public void VarSets_MixedTemplateValue_IsQuoted()
    {
        // Regression: VarSetStringToExpr's fallthrough used to return a mixed literal-text-plus-
        // {var} string unquoted — "Unexpected trailing input: '{'" at load time, since the
        // resulting bare braces aren't valid expr syntax. Real-world crash: Fear of the Unknown's
        // FearoftheUnknownStart/ProperLetterHeading "newspaper" assign (Vars.newspaper = "The " +
        // Vars.townname + " " + macros1.either(...)), extracted as a VarSets {var}-braced template.
        // Now that ExpressionEvaluator interpolates {expr} inside quoted string literals, quoting
        // this is correct (not just crash-avoidance) — see StringLiteral_MixedTextAndMultiple
        // PlaceholdersInterpolates in ExpressionEvaluatorTests for the runtime half of this fix.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.EffectNode
                {
                    VarSets = new() { ["newspaper"] = "The {townname} {_rnd_0}" },
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var expr = (string)Nodes0(d)["expr"]!;

        Assert.Equal("\"The {townname} {_rnd_0}\"", expr);
    }

    [Fact]
    public void OrphanSectionBody_EmitsHubCardStyle()
    {
        // A hub location without its own heading (hubDetails with no preceding hubTitle) still gets
        // the hub-card border — same reasoning as the titled case above.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes = [new Masterwork.Extractor.SectionBodyNode { Nodes = [new Masterwork.Extractor.TextNode { Template = "Body" }] }],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("section", node["type"]);
        Assert.Equal("hub-card", node["style"]);
        Assert.False(node.ContainsKey("title"));
    }

    [Fact]
    public void OnShowBiddingCall_EmitsNestedCountdownPopup()
    {
        // ViewBiddingSystem.instance.OnShowBidding("Target", BiddingSystem.Bidding) — the reference
        // app's countdown-then-reveal component, previously flattened into a bare
        // layout: 'bidding' popup handled by the (now-retired) bespoke VotingPopupContent component.
        // Real shape, matching S5Special1a: an ExpandLinkNode whose only fragment content is the
        // bidding call itself (FragmentStitchPass/PassageBodyVisitor already isolate it this way).
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "click to start the bid...",
                    StateAffecting = false,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.UnknownNode
                        {
                            OriginalCode = "ViewBiddingSystem.instance.OnShowBidding(\"Target\", BiddingSystem.Bidding);",
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var outer = Nodes0(d);

        Assert.Equal("popup", outer["type"]);
        Assert.Equal("countdown_instructions", outer["layout"]);
        Assert.Equal("click to start the bid...", outer["label"]);
        Assert.False(outer.ContainsKey("okay"));
        Assert.False(outer.ContainsKey("cancel"));

        var outerContent = (List<Dictionary<string, object?>>)outer["content"]!;
        var inner = outerContent[^1];
        Assert.Equal("popup", inner["type"]);
        Assert.Equal("countdown_action", inner["layout"]);
        Assert.Equal("Start Bidding", inner["label"]);
        Assert.Equal("Target", inner["target"]);
        Assert.Equal("REVEAL", inner["okay"]);
        Assert.Equal(false, inner["snapshot"]);

        var innerContent = (List<Dictionary<string, object?>>)inner["content"]!;
        Assert.Equal(4, innerContent.Count);
        Assert.Equal(["3", "2", "1", "REVEAL"], innerContent.Select(n => n["value"]));
        Assert.Equal(
            ["countdown-3", "countdown-2", "countdown-1", "countdown-reveal"],
            innerContent.Select(n => n["style"]));
    }

    [Fact]
    public void OnShowBiddingCall_VotingMode_UsesVotingWording()
    {
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "Click here to start the vote...",
                    StateAffecting = true,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.UnknownNode
                        {
                            OriginalCode = "ViewBiddingSystem.instance.OnShowBidding(\"Target\", BiddingSystem.Voting);",
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var outer = Nodes0(d);
        var outerContent = (List<Dictionary<string, object?>>)outer["content"]!;
        var inner = outerContent[^1];

        Assert.Equal("Start Voting", inner["label"]);
        Assert.Equal(true, inner["snapshot"]);
        Assert.Contains("Start Voting", (string)outerContent[0]["value"]!);
    }

    [Fact]
    public void ExpandLink_NoMarker_EmitsRevealLayoutWithCancel()
    {
        // Real shape, matching cost-of-disease's Gen1-CreepyTrackRes (00179): an enchantHook/Replace
        // whose fragment carries no setup/EOG/EOR/bidding marker at all — just narrative text ending
        // in a real choice link. Structurally, this popup would otherwise have no layout AND no way
        // to close itself (no okay/cancel/target) — TransformPopup's final fallback branch gives it
        // layout: 'reveal' plus a generic Cancel so the player can back out.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "Once you are ready, click here to reveal your secret conversation.",
                    StateAffecting = true,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.TextNode { Template = "It was unorthodox for..." },
                        new Masterwork.Extractor.LinkNode { Label = "Accept the offer.", Target = "Yes", StateAffecting = true },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var outer = Nodes0(d);

        Assert.Equal("popup", outer["type"]);
        Assert.Equal("reveal", outer["layout"]);
        Assert.Equal("Close", outer["cancel"]);
        Assert.False(outer.ContainsKey("okay"));
        Assert.False(outer.ContainsKey("target"));
        Assert.Equal(true, outer["snapshot"]);
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
        Assert.Equal("prompt", popup["layout"]);
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
    public void SetupNotificationBlock_SwitchWithLeadingImageAndMoreContent_SplitsIntoParallelSwitches()
    {
        // Regression: Fear of the Unknown's Liberal — the same "each branch sets its own setup
        // image, then diverges further" shape as the ConditionalNode case above, but keyed by a
        // `switch` (on `creature`) instead of an if/else chain. Before this fix, SplitPopupHeaderNodes
        // only recognized ConditionalNode for this promotion, so a switch-shaped setup popup kept its
        // per-case image inline in `content` instead of hoisting it to `header` — the popup rendered
        // with no header image at all, even though the content itself displayed correctly.
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
                        new Masterwork.Extractor.SwitchNode
                        {
                            On = "creature",
                            Cases =
                            [
                                new Masterwork.Extractor.SwitchCase
                                {
                                    Match = 2,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/VillageChronicleCover", Style = "setup-image" },
                                        new Masterwork.Extractor.TextNode { Template = "Turn to page 10." },
                                    ],
                                },
                                new Masterwork.Extractor.SwitchCase
                                {
                                    Match = 1,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/S2_HuntTokenFRONT", Style = "setup-image" },
                                        new Masterwork.Extractor.TextNode { Template = "Turn to page 11." },
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
        var headerSwitch = Assert.Single(header);
        Assert.Equal("switch", headerSwitch["type"]);
        var headerCases = (List<Dictionary<string, object?>>)headerSwitch["cases"]!;
        Assert.Equal(2, headerCases.Count);
        var headerImage2 = Assert.Single((List<Dictionary<string, object?>>)headerCases[0]["nodes"]!);
        Assert.Equal("image", headerImage2["type"]);
        Assert.Equal("image://setup/VillageChronicleCover", headerImage2["asset"]);
        var headerImage1 = Assert.Single((List<Dictionary<string, object?>>)headerCases[1]["nodes"]!);
        Assert.Equal("image://setup/S2_HuntTokenFRONT", headerImage1["asset"]);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        var contentSwitch = Assert.Single(content, n => (string)n["type"]! == "switch");
        var contentCases = (List<Dictionary<string, object?>>)contentSwitch["cases"]!;
        var contentCaseNodes = (List<Dictionary<string, object?>>)contentCases[0]["nodes"]!;
        var contentText = Assert.Single(contentCaseNodes);
        Assert.Equal("text", contentText["type"]);
        Assert.DoesNotContain(contentCaseNodes, n => (string)n["type"]! == "image");
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
    public void SetupNotificationBlock_InterstitialImageBeforeBlock_FoldsIntoHeader()
    {
        // Regression: Cost of Disease's HelpingEvil2 — `Vars._SetupImage = "S1_EstateUpgradeBACK"`
        // sits BEFORE entering `styleScope("setupStyleEvnt", ...)`, not inside it, so it becomes a
        // top-level ImageNode sitting between the SetupNotificationNode and the SetupBlockNode. The
        // pairing loop in TransformNodeList used to only tolerate BreakNode/ParagraphBreakNode there
        // — anything else broke the pairing entirely, falling back to TransformSetupNotification's
        // standalone rendering: an empty `section: panel` (sn.Title/sn.Text are always null for this
        // idiom) plus a bare "Continue" link, with the image and the block's own content scattered as
        // unrelated top-level siblings instead of one coherent popup. The image must now end up in
        // the popup's own header instead.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode(),
                new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/S1_EstateUpgradeBACK", Style = "setup-image" },
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.TextNode { Template = "Then, starting with the player..." },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var nodes = (List<Dictionary<string, object?>>)d["nodes"]!;
        var popup = Assert.Single(nodes, n => (string)n["type"]! == "popup");
        Assert.DoesNotContain(nodes, n => (string)n["type"]! == "section"); // no orphaned empty section
        Assert.DoesNotContain(nodes, n => (string)n["type"]! == "image"); // not left as a stray top-level sibling

        var header = (List<Dictionary<string, object?>>)popup["header"]!;
        var headerImage = Assert.Single(header);
        Assert.Equal("image", headerImage["type"]);
        Assert.Equal("image://setup/S1_EstateUpgradeBACK", headerImage["asset"]);

        var content = (List<Dictionary<string, object?>>)popup["content"]!;
        Assert.Single(content);
        Assert.Equal("text", content[0]["type"]);
    }

    [Fact]
    public void SetupNotificationBlock_InterstitialPlainAssignBeforeBlock_PreservedAsSeparateNode()
    {
        // Regression: Cost of Disease's WolvesSetupGen3 — `Vars.gen3pg = 0;` (an ordinary session-var
        // assign, no visual output of its own) sits between `ViewItemObtain.SetupPassagename = "..."`
        // and `styleScope("setupStyleEvnt", ...)`. Same broken-pairing failure mode as the interstitial
        // image case above, but the fix is different: this node isn't part of the popup's header OR
        // content — it's ordinary body content that happens to sit between the two and must be
        // preserved as its own `assign` node emitted just before the popup, not silently dropped.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode(),
                new Masterwork.Extractor.EffectNode { VarSets = new() { ["gen3pg"] = 0 } },
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/StorybookToken", Style = "setup-image" },
                        new Masterwork.Extractor.TextNode { Template = "Each player retrieves a token." },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var nodes = (List<Dictionary<string, object?>>)d["nodes"]!;
        Assert.DoesNotContain(nodes, n => (string)n["type"]! == "section"); // no orphaned empty section

        var assign = Assert.Single(nodes, n => (string)n["type"]! == "assign");
        Assert.Equal("gen3pg", assign["var"]);
        Assert.Equal("0", assign["expr"]);

        var popup = Assert.Single(nodes, n => (string)n["type"]! == "popup");
        var header = (List<Dictionary<string, object?>>)popup["header"]!;
        var headerImage = Assert.Single(header);
        Assert.Equal("image://setup/StorybookToken", headerImage["asset"]);

        // The assign must come BEFORE the popup, matching its original position in the source.
        Assert.True(nodes.IndexOf(assign) < nodes.IndexOf(popup));
    }

    [Fact]
    public void SetupNotificationBlock_InterstitialAssignOnlyConditionalBeforeBlock_PreservedAsSeparateNode()
    {
        // Regression: Fear of the Unknown's Witchwolf1Hex/Witchwolf2Hex — an if/elseif chain (one
        // branch per witch suspect) between `ViewItemObtain.SetupPassagename = "..."` and
        // `styleScope("setupStyleEvnt", ...)`, each branch just a plain assign choosing which name
        // feeds `witch1`. No branch produces any visible output, so — like the plain EffectNode/LetNode
        // case above — the whole conditional is ordinary bookkeeping that must run before the popup,
        // not a competing header/content candidate.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode(),
                new Masterwork.Extractor.ConditionalNode
                {
                    Branches =
                    [
                        new Masterwork.Extractor.ConditionalBranch
                        {
                            Condition = "witch == nameA",
                            Nodes = [new Masterwork.Extractor.EffectNode { VarSets = new() { ["witch1"] = "witA" } }],
                        },
                        new Masterwork.Extractor.ConditionalBranch
                        {
                            Condition = "witch == nameB",
                            Nodes = [new Masterwork.Extractor.EffectNode { VarSets = new() { ["witch1"] = "witB" } }],
                        },
                    ],
                },
                new SetupBlockNode
                {
                    Nodes =
                    [
                        new Masterwork.Extractor.ImageNode { AssetRef = "image://setup/S2_WitchHexToken", Style = "setup-image" },
                        new Masterwork.Extractor.TextNode { Template = "Retrieve the token." },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var nodes = (List<Dictionary<string, object?>>)d["nodes"]!;
        Assert.DoesNotContain(nodes, n => (string)n["type"]! == "section"); // no orphaned empty section

        var cond = Assert.Single(nodes, n => (string)n["type"]! == "conditional");
        var popup = Assert.Single(nodes, n => (string)n["type"]! == "popup");
        Assert.True(nodes.IndexOf(cond) < nodes.IndexOf(popup));

        var header = (List<Dictionary<string, object?>>)popup["header"]!;
        var headerImage = Assert.Single(header);
        Assert.Equal("image://setup/S2_WitchHexToken", headerImage["asset"]);
    }

    [Fact]
    public void SetupNotificationBlock_InterstitialConditionalWithRealContent_NotRecognized_FallsBackToStandalone()
    {
        // Contrast: a conditional sitting between the two that DOES produce real visible output in
        // one of its branches is a genuinely different, more ambiguous shape than "some bookkeeping
        // happens first" — IsPreservableSetupPreamble must reject it, same conservative "don't guess"
        // fallback as an entirely unrecognized interstitial node.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new SetupNotificationNode(),
                new Masterwork.Extractor.ConditionalNode
                {
                    Branches =
                    [
                        new Masterwork.Extractor.ConditionalBranch
                        {
                            Condition = "witch == nameA",
                            Nodes = [new Masterwork.Extractor.TextNode { Template = "Real visible text." }],
                        },
                    ],
                },
                new SetupBlockNode
                {
                    Nodes = [new Masterwork.Extractor.TextNode { Template = "Retrieve the token." }],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var nodes = (List<Dictionary<string, object?>>)d["nodes"]!;
        // Falls back to the standalone shape: an empty `section: panel` (sn.Title/sn.Text are both
        // null for this idiom) instead of merging into one coherent popup with the notification's
        // own target info — the tell-tale symptom of an unrecognized interstitial.
        var section = Assert.Single(nodes, n => (string)n["type"]! == "section");
        Assert.Empty((List<Dictionary<string, object?>>)section["content"]!);
        // The conditional's own real content must still survive, not be silently dropped.
        var cond = Assert.Single(nodes, n => (string)n["type"]! == "conditional");
        var condThen = (List<Dictionary<string, object?>>)cond["then"]!;
        Assert.Contains(condThen, n => (string)n["type"]! == "text");
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

    [Fact]
    public void ExpandLink_SetupPassagenameInSiblingConditionals_CollapsesToSetupPopupWithTernaryTarget()
    {
        // Regression: Fear of the Unknown's Payment1ThanksB passage (source lines 19902-19931).
        // ViewItemObtain.SetupPassagename set inside TWO SEPARATE sibling `if` statements — no else
        // at all ("if (players > 2) ...; if (players == 2) ...;", technically exhaustive since
        // players is always >= 2, but the extractor can't verify that from syntax alone) — instead
        // of one if/elseif/else chain. TransformPopup's own top-level scan only recognized a
        // SetupNotificationNode as a direct child, never one nested inside a ConditionalNode, so
        // this fell through as ordinary conditional content: layout stayed unset (defaulting to
        // reveal) and no target/okay were ever produced — a broken popup a player could open but
        // never correctly navigate away from.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "Click to continue...",
                    StateAffecting = true,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "players > 2",
                                    Nodes = [new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Payment1C" }],
                                },
                            ],
                        },
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "players == 2",
                                    Nodes = [new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Payment1Hub" }],
                                },
                            ],
                        },
                        new Masterwork.Extractor.SetupBlockNode
                        {
                            Nodes = [new Masterwork.Extractor.TextNode { Template = "Immediately pay ${Bcont} to the supply." }],
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("popup", node["type"]);
        Assert.Equal("setup", node["layout"]);
        Assert.Equal("${players > 2 ? \"Payment1C\" : players == 2 ? \"Payment1Hub\" : \"\"}", node["target"]);
        // "Close" (not "Accept") is the established label for any setup popup that has a target —
        // see the pre-existing S4Blacksmith example; "Accept" is only for a target-less one.
        Assert.Equal("Close", node["okay"]);

        var content = (List<Dictionary<string, object?>>)node["content"]!;
        Assert.Contains(content, c => c["type"] as string == "text");
    }

    [Fact]
    public void ExpandLink_SetupPassagenameIfElseWithInlineEitherBranch_CollapsesTargetWithNoHoisting()
    {
        // Regression: Fear of the Unknown's EssentialSaltsRes passage (source lines 14014-14029):
        // "if (Vars.round == 19) { ViewItemObtain.SetupPassagename = "LiberalEvent3"; } else {
        // ViewItemObtain.SetupPassagename = macros1.either("LiberalEvent", "LiberalTaxes"); }" — one
        // branch is a literal target, the other assigns from an inline either() call. The buggy
        // original: the else branch's target was the raw, untranslated "${macros1.either(...)}"
        // text, so BuildTernaryChain's naive re-quoting double-wrapped it as a literal string
        // containing an unevaluated "${...}" fragment, never actually evaluated at render time.
        //
        // Two earlier fixes hoisted the draw into a preamble node sitting before the branch's
        // SetupNotificationNode — a `let` (reverted: doesn't survive a popup's own content→target
        // scope boundary — see git history) and then a session `assign` (worked, but needed
        // TryGetSetupTargetArms's branch pattern to tolerate a preamble node at all, which regressed
        // on A Time of War's BlameResolve/ParadoxFirst — see git history). Neither hoist was actually
        // needed: SetupNotificationNode carries the VarRandom directly (no preamble, no extra node —
        // each branch here is still exactly one node), and V2Serializer.ResolveSetupTarget resolves
        // it into a bare ${...}-free expression fragment that BuildTernaryChain's TargetExpr embeds
        // directly into the ternary, the same way it already handles the "complex/ternary fallback"
        // case for a plain computed-expression SetupPassagename.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "Click to continue...",
                    StateAffecting = true,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "round == 19",
                                    Nodes = [new Masterwork.Extractor.SetupNotificationNode { NextPassage = "LiberalEvent3" }],
                                },
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Else = true,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.SetupNotificationNode
                                        {
                                            Random = new Masterwork.Extractor.VarRandom
                                            {
                                                RandomType = "choose-one",
                                                Values = ["LiberalEvent", "LiberalTaxes"],
                                                SeedKey = "P1_0",
                                            },
                                        },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("popup", node["type"]);
        Assert.Equal("setup", node["layout"]);
        // A clean single-level ternary — no nested "${...}" string inside the outer expression.
        Assert.Equal(
            "${round == 19 ? \"LiberalEvent3\" : [\"LiberalEvent\", \"LiberalTaxes\"].shuffled(\"P1_0\")[0]}",
            node["target"]);
        Assert.Equal("Close", node["okay"]);

        // No extra node landed in content — nothing was hoisted, so there's nothing to place or
        // scope correctly in the first place.
        var content = node.GetValueOrDefault("content") as List<Dictionary<string, object?>>;
        Assert.True(content is null or []);
    }

    [Fact]
    public void ExpandLink_SwitchWithPerCasePreambleBeforeSetupPassagename_PromotesTargetAndKeepsAssignsGated()
    {
        // Regression: A Time of War's PackingHeat1a (source lines 14579-14600) — "switch (round) {
        // case 7: martweapons = 1; martial = 1; ViewItemObtain.SetupPassagename = "Martial1"; break;
        // case 8: martweapons = 1; martial = 2; SetupPassagename = "Martial2"; break; default:
        // martweapons = 1; martial = 3; SetupPassagename = "Martial3"; break; }" — each case has
        // per-branch assigns BEFORE the SetupPassagename, unlike Payment1ThanksB/EssentialSaltsRes
        // above (bare single-node branches only). TryGetSetupTargetArms used to require an exact
        // one-node-per-branch match, so this whole switch fell through as ordinary content: no
        // layout: setup, no target, and the popup could be opened but never correctly navigated away
        // from. The fix strips only the trailing SetupNotificationNode from each case — the assigns
        // must stay exactly where they were, still gated by their own case, never hoisted out
        // unconditionally (see git history: an earlier attempt at hoisting corrupted A Time of War's
        // BlameResolve/ParadoxFirst by running a branch's preamble regardless of which branch matched).
        var passage = new MwsPassage
        {
            PassageId = "PackingHeat1a",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "Click to continue...",
                    StateAffecting = true,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.SwitchNode
                        {
                            On = "round",
                            Cases =
                            [
                                new Masterwork.Extractor.SwitchCase
                                {
                                    Match = 7,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martweapons"] = "1" } },
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martial"] = "1" } },
                                        new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Martial1" },
                                    ],
                                },
                                new Masterwork.Extractor.SwitchCase
                                {
                                    Match = 8,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martweapons"] = "1" } },
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martial"] = "2" } },
                                        new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Martial2" },
                                    ],
                                },
                                new Masterwork.Extractor.SwitchCase
                                {
                                    Default = true,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martweapons"] = "1" } },
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martial"] = "3" } },
                                        new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Martial3" },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("popup", node["type"]);
        Assert.Equal("setup", node["layout"]);
        Assert.Equal(
            "${round == 7 ? \"Martial1\" : round == 8 ? \"Martial2\" : \"Martial3\"}",
            node["target"]);
        Assert.Equal("Close", node["okay"]);

        var content = (List<Dictionary<string, object?>>)node["content"]!;
        var sw = Assert.Single(content, c => c["type"] as string == "switch");
        Assert.DoesNotContain(content, c => c["type"] as string == "setup_notification");

        var cases = (List<Dictionary<string, object?>>)sw["cases"]!;
        var case7 = Assert.Single(cases, c => Equals(c["match"], 7));
        var case7Nodes = (List<Dictionary<string, object?>>)case7["nodes"]!;
        Assert.Equal(2, case7Nodes.Count); // both assigns, SetupNotificationNode stripped
        Assert.All(case7Nodes, n => Assert.Equal("assign", n["type"]));

        var defaultNodes = (List<Dictionary<string, object?>>)sw["default"]!;
        Assert.Equal(2, defaultNodes.Count);
    }

    [Fact]
    public void ExpandLink_ConditionalWithPreambleBeforeSetupPassagename_KeepsAssignGatedByItsOwnBranch()
    {
        // Same preamble-tolerance fix as the SwitchNode test above, but for the ConditionalNode
        // overload of TryGetSetupTargetArms (an if/else chain rather than a switch).
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "Click to continue...",
                    StateAffecting = true,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.ConditionalNode
                        {
                            Branches =
                            [
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Condition = "round == 7",
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martial"] = "1" } },
                                        new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Martial1" },
                                    ],
                                },
                                new Masterwork.Extractor.ConditionalBranch
                                {
                                    Else = true,
                                    Nodes =
                                    [
                                        new Masterwork.Extractor.EffectNode { VarSets = new() { ["martial"] = "3" } },
                                        new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Martial3" },
                                    ],
                                },
                            ],
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("popup", node["type"]);
        Assert.Equal("setup", node["layout"]);
        Assert.Equal("${round == 7 ? \"Martial1\" : \"Martial3\"}", node["target"]);

        var content = (List<Dictionary<string, object?>>)node["content"]!;
        var cond = Assert.Single(content, c => c["type"] as string == "conditional");
        // The assign stays inside the (still-gated) branch — never hoisted to a top-level sibling —
        // and the SetupNotificationNode is gone (its info now lives in the popup's own target).
        var thenNodes = (List<Dictionary<string, object?>>)cond["then"]!;
        var assign = Assert.Single(thenNodes);
        Assert.Equal("assign", assign["type"]);
        Assert.Equal("martial", assign["var"]);
    }

    [Fact]
    public void ExpandLink_SwitchWithNoLeftoverContent_OmitsSwitchEntirely()
    {
        // Contrast with the preamble tests above: when every case is STILL just a bare
        // SetupNotificationNode (the original Payment1ThanksB/SeedGUNS shape, just expressed as a
        // SwitchNode instead of chained ifs), there's nothing left to keep — the switch itself must
        // be omitted, matching the existing ConditionalNode-chain behavior tested above.
        var passage = new MwsPassage
        {
            PassageId = "P1",
            Nodes =
            [
                new Masterwork.Extractor.ExpandLinkNode
                {
                    Label = "Click to continue...",
                    StateAffecting = true,
                    ExpandNodes =
                    [
                        new Masterwork.Extractor.SwitchNode
                        {
                            On = "round",
                            Cases =
                            [
                                new Masterwork.Extractor.SwitchCase { Match = 7, Nodes = [new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Martial1" }] },
                                new Masterwork.Extractor.SwitchCase { Default = true, Nodes = [new Masterwork.Extractor.SetupNotificationNode { NextPassage = "Martial3" }] },
                            ],
                        },
                    ],
                },
            ],
        };

        var d = V2Serializer.ToDict(passage);
        var node = Nodes0(d);

        Assert.Equal("setup", node["layout"]);
        Assert.Equal("${round == 7 ? \"Martial1\" : \"Martial3\"}", node["target"]);

        var content = node.GetValueOrDefault("content") as List<Dictionary<string, object?>>;
        Assert.True(content is null or []);
    }
}
