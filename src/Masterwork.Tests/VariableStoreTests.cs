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
            ["round"] = new() { Name = "round", VarType = "int", Default = 1L },
        };
        var store = MakeStore(manifest);
        Assert.Equal(1L, store.GetVariable("round").AsInt());
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
}
