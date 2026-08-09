using Masterwork.Engine;
using Masterwork.Engine.Expressions;
using Masterwork.Engine.Session;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class VariableStoreTests
{
    private static VariableStore MakeStore(Dictionary<string, VarDef>? manifest = null) =>
        new(manifest ?? [], new SessionPrng(42));

    [Fact]
    public void SetGet_Int()
    {
        var store = MakeStore();
        store.SetSessionVariable("round", StoryValue.Of(3L));
        Assert.Equal(3L, store.GetVariable("round").AsInt());
    }

    [Fact]
    public void SetGet_String()
    {
        var store = MakeStore();
        store.SetSessionVariable("wolves", StoryValue.Of("evil"));
        Assert.Equal("evil", store.GetVariable("wolves").AsString());
    }

    [Fact]
    public void Default_UsedWhenUnset()
    {
        var manifest = new Dictionary<string, VarDef>
        {
            ["round"] = new() { Name = "round", VarType = VarKind.Integer, Default = 1L },
        };
        var store = MakeStore(manifest);
        Assert.Equal(1L, store.GetVariable("round").AsInt());
    }

    [Fact]
    public void Default_CanonicalZero_UsedForEachVarKind_WhenNoExplicitDefault()
    {
        var manifest = new Dictionary<string, VarDef>
        {
            ["s"] = new() { Name = "s", VarType = VarKind.String },
            ["i"] = new() { Name = "i", VarType = VarKind.Integer },
            ["b"] = new() { Name = "b", VarType = VarKind.Boolean },
            ["r"] = new() { Name = "r", VarType = VarKind.Record },
            ["sa"] = new() { Name = "sa", VarType = VarKind.StringArray },
            ["ia"] = new() { Name = "ia", VarType = VarKind.IntArray },
            ["ba"] = new() { Name = "ba", VarType = VarKind.BooleanArray },
            ["ra"] = new() { Name = "ra", VarType = VarKind.RecordArray },
        };
        var store = MakeStore(manifest);

        Assert.Equal("", store.GetVariable("s").AsString());
        Assert.Equal(0L, store.GetVariable("i").AsInt());
        Assert.False(store.GetVariable("b").AsBool());
        Assert.Empty(store.GetVariable("r").AsRecord());
        Assert.Empty(store.GetVariable("sa").AsArray());
        Assert.Empty(store.GetVariable("ia").AsArray());
        Assert.Empty(store.GetVariable("ba").AsArray());
        Assert.Empty(store.GetVariable("ra").AsArray());
    }

    [Fact]
    public void Default_ExplicitBoolDefault_Honored()
    {
        var manifest = new Dictionary<string, VarDef>
        {
            ["flag"] = new() { Name = "flag", VarType = VarKind.Boolean, Default = true },
        };
        var store = MakeStore(manifest);
        Assert.True(store.GetVariable("flag").AsBool());
    }

    [Fact]
    public void TemplateExpansion_IntVar()
    {
        var store = MakeStore();
        store.SetSessionVariable("round", StoryValue.Of(3L));
        Assert.Equal("3", store.ExpandTemplate("{round}"));
    }

    [Fact]
    public void TemplateExpansion_StringVar()
    {
        var store = MakeStore();
        store.SetSessionVariable("wolves", StoryValue.Of("evil"));
        Assert.Equal("evil", store.ExpandTemplate("{wolves}"));
    }

    [Fact]
    public void TemplateExpansion_IconRef()
    {
        var store = MakeStore();
        Assert.Equal("{icon:angrymob_icon}", store.ExpandTemplate("{icon:angrymob_icon}"));
    }

    [Fact]
    public void TemplateExpansion_ArrayIndex()
    {
        var store = MakeStore();
        store.SetSessionVariable("elim", StoryValue.Of(new List<StoryValue> { StoryValue.Of("a"), StoryValue.Of("b") }));
        Assert.Equal("a", store.ExpandTemplate("{elim[0]}"));
    }

    [Fact]
    public void TemplateExpansion_DotProperty()
    {
        var store = MakeStore();
        var entry = new StoryValue.RecordVal(new Dictionary<string, StoryValue> { ["player_name"] = StoryValue.Of("Alice") });
        store.SetLetVariable("entry", entry);
        Assert.Equal("Alice", store.ExpandTemplate("{entry.player_name}"));
    }

    [Fact]
    public void LetVar_Isolated_FromSession()
    {
        var store = MakeStore();
        store.SetLetVariable("tempVal", StoryValue.Of(5L));
        Assert.DoesNotContain("tempVal", store.SessionSnapshot().Keys);
    }

    [Fact]
    public void LetVar_VisibleDuringRender()
    {
        var store = MakeStore();
        store.SetLetVariable("tempVal", StoryValue.Of(5L));
        Assert.Equal(5L, store.GetVariable("tempVal").AsInt());
    }

    [Fact]
    public void LetVar_DiscardedAfterRender()
    {
        var store = MakeStore();
        store.SetLetVariable("tempVal", StoryValue.Of(5L));
        store.ClearLetScope();
        Assert.Throws<StoryEvalException>(() => store.GetVariable("tempVal"));
    }

    [Fact]
    public void CommitChangesTo_AppliesOnlyWhatTheCloneItselfChanged()
    {
        var live = MakeStore();
        live.SetSessionVariable("round", StoryValue.Of(1L));
        live.SetSessionVariable("sepinc1", StoryValue.Of(""));

        var sandbox = live.Clone();
        sandbox.SetSessionVariable("round", StoryValue.Of(2L)); // popup's own content assign

        sandbox.CommitChangesTo(live);

        Assert.Equal(2L, live.GetVariable("round").AsInt());
        Assert.Equal("", live.GetVariable("sepinc1").AsString());
    }

    [Fact]
    public void CommitChangesTo_DoesNotOverwriteALiveChangeMadeAfterTheCloneWasTaken()
    {
        // Regression: A Time of War's AdvancedWeaponryIntro — a popup's sandbox is cloned partway
        // through rendering the passage; sibling `assign` nodes positioned AFTER that popup in the
        // SAME passage's own node list run directly against the live store afterward, during the
        // SAME render, well before the player ever sees/accepts the popup. The sandbox has no idea
        // that later assign happened — a wholesale RestoreSession-style replace at accept time would
        // silently wipe it out. CommitChangesTo must only apply what THIS sandbox itself changed
        // (relative to its own Clone()-time baseline), leaving any independent later live change —
        // like sepinc1's assign here — untouched.
        var live = MakeStore();
        live.SetSessionVariable("sepinc1", StoryValue.Of(""));

        var sandbox = live.Clone(); // popup rendered here, before sepinc1's own assign runs
        live.SetSessionVariable("sepinc1", StoryValue.Of("Gained a Servant")); // trailing sibling assign

        sandbox.CommitChangesTo(live); // player accepts the popup

        Assert.Equal("Gained a Servant", live.GetVariable("sepinc1").AsString());
    }

    [Fact]
    public void CommitChangesTo_NotCalledOnAClone_Throws()
    {
        var store = MakeStore();
        Assert.Throws<InvalidOperationException>(() => store.CommitChangesTo(MakeStore()));
    }

    [Fact]
    public void CommitChangesTo_NestedClone_CommitsChangeMadeByAncestorSandbox()
    {
        // Regression: A Time of War's RumorD2 — `assign rumor2 = "visited"` sits in the OUTER
        // (`layout: reveal`) popup's own content, ahead of a nested `layout: setup` popup that's
        // the player's only way to actually leave (the outer has no `okay` of its own — see
        // docs/mws-format-latest.md §6's nested-popup pattern). Only the INNER popup ever gets
        // ClosePopupAsync'd. Before this fix, the inner sandbox's OWN Clone()-time baseline was
        // taken from the OUTER sandbox's state AFTER `rumor2 = "visited"` already ran — so the
        // change was already "baked in" as the inner sandbox's own starting point, invisible to a
        // same-level before/after diff. rumor2 never reached the live store; RumorD2 kept
        // reappearing every time RumorD was revisited instead of exactly once per game.
        var live = MakeStore();
        live.SetSessionVariable("rumor2", StoryValue.Of(""));

        var outerSandbox = live.Clone(); // outer "reveal" popup rendered here
        outerSandbox.SetSessionVariable("rumor2", StoryValue.Of("visited")); // its own content assign
        var innerSandbox = outerSandbox.Clone(); // nested "setup" popup, cloned from the outer's sandbox

        innerSandbox.CommitChangesTo(live); // player accepts only the INNER popup

        Assert.Equal("visited", live.GetVariable("rumor2").AsString());
    }

    [Fact]
    public void CommitChangesTo_NestedClone_StillIgnoresAValueTheNestedSandboxNeverChanged()
    {
        // Contrast: a variable the OUTER sandbox never touched shouldn't suddenly count as
        // "changed" just because the baseline now propagates from further up the chain.
        var live = MakeStore();
        live.SetSessionVariable("round", StoryValue.Of(1L));
        live.SetSessionVariable("rumor2", StoryValue.Of(""));

        var outerSandbox = live.Clone();
        outerSandbox.SetSessionVariable("rumor2", StoryValue.Of("visited"));
        var innerSandbox = outerSandbox.Clone();

        innerSandbox.CommitChangesTo(live);

        Assert.Equal(1L, live.GetVariable("round").AsInt());
    }

    [Fact]
    public void CommitChangesTo_NestedClone_StillIgnoresALiveChangeMadeAfterTheOuterCloneWasTaken()
    {
        // The AdvancedWeaponryIntro protection (a live-store change made independently, after the
        // outermost clone point, must survive a later nested commit untouched) still holds once the
        // baseline propagates through more than one level of nesting.
        var live = MakeStore();
        live.SetSessionVariable("sepinc1", StoryValue.Of(""));

        var outerSandbox = live.Clone();
        var innerSandbox = outerSandbox.Clone();
        live.SetSessionVariable("sepinc1", StoryValue.Of("Gained a Servant")); // trailing sibling assign

        innerSandbox.CommitChangesTo(live);

        Assert.Equal("Gained a Servant", live.GetVariable("sepinc1").AsString());
    }
}
