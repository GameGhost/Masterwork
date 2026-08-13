using Masterwork.Engine;
using Masterwork.Engine.Audio;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class AudioResolverTests
{
    private static VariableStore EmptyStore() => new(new Dictionary<string, VarDef>(), new SessionPrng(1));

    private static PassageRenderResult MakeRender(string? music = null, IReadOnlyList<RenderedAction>? actions = null) =>
        new(
            PassageId: "P1",
            Layout: "narration",
            Title: null,
            Subtitle: null,
            LocationName: null,
            LocationIcon: null,
            Nodes: [],
            Actions: actions ?? [],
            Checkpoints: [],
            PendingGoto: null,
            Chrome: RenderedLayoutChrome.Empty)
        {
            Music = music,
        };

    private static RenderedPopup MakePopup(string id, string? music = null, IReadOnlyList<RenderedAction>? nestedActions = null) =>
        new()
        {
            Id = id,
            Header = [],
            Content = [],
            Actions = nestedActions ?? [],
            Sandbox = EmptyStore(),
            OnCloseRaw = [],
            StateAffecting = false,
            Chrome = RenderedLayoutChrome.Empty,
            Audio = music is null ? null : new RenderedPopupAudio { Music = music },
        };

    private static ModuleAudioManifest MakeModuleAudio(IReadOnlyList<string>? tracks = null, string order = "sequence") =>
        new() { Music = new ModuleMusicManifest { DefaultTracks = tracks ?? [], Order = order } };

    [Fact]
    public void NoOverridesAnywhere_ModuleHasNoTracks_ResolvesToSilence()
    {
        var render = MakeRender();
        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string>(), moduleAudio: null);
        Assert.IsType<AudioResolution.Silence>(resolution);
    }

    [Fact]
    public void ModuleDefault_SingleTrack_Wins_WhenNothingElseOverrides()
    {
        var render = MakeRender();
        var moduleAudio = MakeModuleAudio(["audio://bgm/theme"]);

        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string>(), moduleAudio);

        var single = Assert.IsType<AudioResolution.SingleTrack>(resolution);
        Assert.Equal("audio://bgm/theme", single.Url);
    }

    [Fact]
    public void ModuleDefault_MultipleTracks_ResolvesToPlaylist()
    {
        var render = MakeRender();
        var moduleAudio = MakeModuleAudio(["audio://bgm/a", "audio://bgm/b"], order: "shuffle");

        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string>(), moduleAudio);

        var playlist = Assert.IsType<AudioResolution.ModulePlaylist>(resolution);
        Assert.Equal(["audio://bgm/a", "audio://bgm/b"], playlist.Tracks);
        Assert.Equal("shuffle", playlist.Order);
    }

    [Fact]
    public void PassageMusic_OverridesModuleDefault()
    {
        var render = MakeRender(music: "audio://bgm/passage_theme");
        var moduleAudio = MakeModuleAudio(["audio://bgm/module_theme"]);

        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string>(), moduleAudio);

        var single = Assert.IsType<AudioResolution.SingleTrack>(resolution);
        Assert.Equal("audio://bgm/passage_theme", single.Url);
    }

    [Fact]
    public void PassageMusic_ExplicitEmpty_ResolvesToSilence_NotModuleFallback()
    {
        var render = MakeRender(music: "");
        var moduleAudio = MakeModuleAudio(["audio://bgm/module_theme"]);

        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string>(), moduleAudio);

        Assert.IsType<AudioResolution.Silence>(resolution);
    }

    [Fact]
    public void OpenPopup_Music_OverridesPassage()
    {
        var popup = MakePopup("popup_0", music: "audio://bgm/tension");
        var render = MakeRender(music: "audio://bgm/passage_theme", actions: [popup]);

        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string> { "popup_0" }, moduleAudio: null);

        var single = Assert.IsType<AudioResolution.SingleTrack>(resolution);
        Assert.Equal("audio://bgm/tension", single.Url);
    }

    [Fact]
    public void UnopenedPopup_DoesNotParticipate()
    {
        // The popup's content (and its own audio.music) is rendered eagerly regardless of whether
        // it's actually open — see RenderedPopup's remarks — but it must not affect the music stack
        // until the player has actually opened it.
        var popup = MakePopup("popup_0", music: "audio://bgm/tension");
        var render = MakeRender(music: "audio://bgm/passage_theme", actions: [popup]);

        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string>(), moduleAudio: null);

        var single = Assert.IsType<AudioResolution.SingleTrack>(resolution);
        Assert.Equal("audio://bgm/passage_theme", single.Url);
    }

    [Fact]
    public void OpenPopup_NoOwnMusic_FallsThroughToPassage()
    {
        var popup = MakePopup("popup_0", music: null);
        var render = MakeRender(music: "audio://bgm/passage_theme", actions: [popup]);

        var resolution = AudioResolver.ResolveMusic(render, new HashSet<string> { "popup_0" }, moduleAudio: null);

        var single = Assert.IsType<AudioResolution.SingleTrack>(resolution);
        Assert.Equal("audio://bgm/passage_theme", single.Url);
    }

    [Fact]
    public void NestedOpenPopup_OverridesOuterOpenPopup()
    {
        var inner = MakePopup("popup_inner", music: "audio://bgm/inner");
        var outer = MakePopup("popup_outer", music: "audio://bgm/outer", nestedActions: [inner]);
        var render = MakeRender(music: "audio://bgm/passage_theme", actions: [outer]);

        var resolution = AudioResolver.ResolveMusic(
            render, new HashSet<string> { "popup_outer", "popup_inner" }, moduleAudio: null);

        var single = Assert.IsType<AudioResolution.SingleTrack>(resolution);
        Assert.Equal("audio://bgm/inner", single.Url);
    }

    [Fact]
    public void NestingFollowsTreeStructure_NotHashSetIterationOrder()
    {
        // expandedPopupIds is a plain unordered HashSet<string> — nesting depth must come from the
        // render tree, not from however the set happens to enumerate. Insert in an order that would
        // give the wrong answer if iteration order were mistaken for nesting depth.
        var inner = MakePopup("popup_inner", music: "audio://bgm/inner");
        var outer = MakePopup("popup_outer", music: "audio://bgm/outer", nestedActions: [inner]);
        var render = MakeRender(actions: [outer]);
        var expanded = new HashSet<string> { "popup_inner", "popup_outer" };

        var resolution = AudioResolver.ResolveMusic(render, expanded, moduleAudio: null);

        var single = Assert.IsType<AudioResolution.SingleTrack>(resolution);
        Assert.Equal("audio://bgm/inner", single.Url);
    }
}
