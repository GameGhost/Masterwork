using System.Text.Json;
using Masterwork.Engine;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class GameSessionTests
{
    private static (GameSession session, LoadedModule module) MakeSimpleSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Welcome'
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Second passage'
            """,
        ]);
        return (new GameSession(module, masterSeed: 1), module);
    }

    private static async Task<(GameSession session, LoadedModule module)> MakeThreeStepSessionAsync()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '1'
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '2'
            - type: 'text'
              value: 'Round {round}'
            - type: 'link'
              label: 'Go'
              target: 'P3'
              snapshot: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P3'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'the end'
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var nav1 = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;
        await session.FollowLinkAsync(nav1);
        var nav2 = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;
        await session.FollowLinkAsync(nav2);
        return (session, module);
    }

    private static GameSession MakeInputSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'input'
              label: 'Enter name'
              var: 'playerName'
            - type: 'link'
              label: 'Submit'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Hello {playerName}'
            """,
        ]);
        return new GameSession(module, masterSeed: 1);
    }

    private static GameSession MakePopupWithInputSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              label: 'Open'
              snapshot: true
              target: 'P2'
              okay: 'Continue'
              content:
              - type: 'input'
                label: 'Score'
                var: 'score'
                min: 0
                max: 10
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Score was {score}'
            """,
        ],
        variablesYaml: """
            standard_variables: []
            variables:
              score:
                type: 'int'
                default: 0
            """);
        return new GameSession(module, masterSeed: 1);
    }

    private static GameSession MakeBooleanInputSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'input'
              label: 'Completed Masterwork'
              var: 'completedMasterwork'
            - type: 'link'
              label: 'Submit'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Completed: {completedMasterwork}'
            """,
        ],
        variablesYaml: """
            standard_variables: []
            variables:
              completedMasterwork:
                type: 'bool'
            """);
        return new GameSession(module, masterSeed: 1);
    }

    private static GameSession MakePopupSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '1'
            - type: 'popup'
              label: 'Open'
              snapshot: true
              target: 'P2'
              content:
              - type: 'assign'
                var: 'round'
                expr: '99'
              - type: 'text'
                value: 'popup body'
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'after popup'
            """,
        ]);
        return new GameSession(module, masterSeed: 1);
    }

    // ── Starting ─────────────────────────────────────────────────────────────

    [Fact]
    public void Start_CreatesGameStartSnapshot()
    {
        var (session, _) = MakeSimpleSession();
        Assert.Equal(SnapshotKind.GameStart, session.Timeline[0].Kind);
    }

    [Fact]
    public void Start_RendersStartPassage()
    {
        var (session, _) = MakeSimpleSession();
        Assert.Equal("P1", session.CurrentRender.PassageId);
    }

    [Fact]
    public void Start_HistoryIndex_Is_Zero()
    {
        var (session, _) = MakeSimpleSession();
        Assert.Equal(0, session.HistoryIndex);
    }

    // ── Following links ──────────────────────────────────────────────────────

    [Fact]
    public async Task FollowLink_StateAffecting_CreatesSnapshot()
    {
        var (session, _) = MakeSimpleSession();
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;
        await session.FollowLinkAsync(navId);
        Assert.Equal(1, session.HistoryIndex);
        Assert.Equal(2, session.Timeline.Count);
    }

    [Fact]
    public async Task FollowLink_StateAffecting_SnapshotCapturesVarState()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        Assert.Equal(1L, session.Timeline[1].Variables["round"].AsInt());
    }

    [Fact]
    public async Task FollowLink_NonStateAffecting_NoSnapshot()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: false
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;
        var countBefore = session.Timeline.Count;

        await session.FollowLinkAsync(navId);

        Assert.Equal(countBefore, session.Timeline.Count);
    }

    [Fact]
    public async Task FollowLink_RendersNewPassage()
    {
        var (session, _) = MakeSimpleSession();
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;
        var result = await session.FollowLinkAsync(navId);
        Assert.Equal("P2", result.PassageId);
    }

    [Fact]
    public async Task FollowLink_StateAffecting_DisplayLabel_DefaultsToDestinationPassageTitle()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            title: 'The Second Passage'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        await session.FollowLinkAsync(navId);

        Assert.Equal("The Second Passage", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task FollowLink_StateAffecting_DisplayLabel_CombinesTitleAndSubtitle()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            title: 'YELLOW FEVER'
            subtitle: 'Early Years'
            layout: 'hub'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        await session.FollowLinkAsync(navId);

        Assert.Equal("YELLOW FEVER - Early Years", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task FollowLink_StateAffecting_DisplayLabel_ExpandsTemplatePlaceholdersInTitle()
    {
        // Regression: DisplayLabel used to read the destination's raw MwsPassageDoc.Title/.Subtitle
        // text directly (ResolvePassageTitle, since removed) without ever expanding "{expr}"
        // placeholders — the on-screen title (PassageRenderResult.Title, via PassageRenderer's own
        // ExpandOrNull/ExpandTemplate call) already expanded them correctly, so a title like
        // "Journal of {randomname}" rendered fine on the page but showed up as the literal,
        // unexpanded "{randomname}" text in the timeline scrubber. Fixed by reading
        // result.Title/.Subtitle (ResolveDisplayLabel) — already expanded, from the same render.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'randomname'
              expr: '"Alice"'
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            title: 'Journal of {randomname}'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        await session.FollowLinkAsync(navId);

        Assert.Equal("Journal of Alice", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task FollowLink_StateAffecting_DisplayLabel_FallsBackToPassageId_WhenNoTitle()
    {
        var (session, _) = MakeSimpleSession();
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        await session.FollowLinkAsync(navId);

        Assert.Equal("P2", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task FollowLink_TimelineLabel_OverridesDestinationPassageTitle()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: 'You chose to lie'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            title: 'The Second Passage'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        await session.FollowLinkAsync(navId);

        Assert.Equal("You chose to lie", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task FollowLink_PreemptingGotoTimelineLabel_OverridesLinkTimelineLabel()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: 'link label'
              onclick:
              - type: 'goto'
                target: 'P3'
                snapshot: 'goto label'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P3'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        var result = await session.FollowLinkAsync(navId);

        Assert.Equal("P3", result.PassageId);
        Assert.Equal("goto label", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task FollowLink_PreemptingGotoWithNoLabel_FallsBackToLinkTimelineLabel()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: 'link label'
              onclick:
              - type: 'goto'
                target: 'P3'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P3'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        var result = await session.FollowLinkAsync(navId);

        Assert.Equal("P3", result.PassageId);
        Assert.Equal("link label", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task FollowLink_NoTarget_GotoInOnclickNavigates()
    {
        // A link can omit target entirely when a goto buried in onclick is guaranteed to fire —
        // e.g. Cost of Disease's HospitalVisitCheck: an assign followed by an exhaustive
        // if/elseif/else chain where every branch ends in a goto.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              snapshot: true
              onclick:
              - type: 'assign'
                var: 'round'
                expr: '1'
              - type: 'conditional'
                if: 'round == 1'
                then:
                - type: 'goto'
                  target: 'P2'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        var result = await session.FollowLinkAsync(navId);

        Assert.Equal("P2", result.PassageId);
        Assert.Equal(1L, session.Current.Variables["round"].AsInt());
    }

    [Fact]
    public async Task FollowLink_NoTargetAndNoGoto_CommitsStateWithoutNavigating()
    {
        // Mirrors PopupAccept_NoTargetOrOnclose_ClosesWithoutReRenderingPassage's own no-destination
        // case: following a link whose onclick never reaches a goto (and which has no target of its
        // own) still runs its effects against the live store, but has nothing to navigate to.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              snapshot: true
              onclick:
              - type: 'assign'
                var: 'round'
                expr: '99'
            - type: 'link'
              label: 'Continue'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var beforeRender = session.CurrentRender;
        var navId = beforeRender.Actions.OfType<RenderedLink>().First().Id;

        var result = await session.FollowLinkAsync(navId);

        Assert.Same(beforeRender, result);
        Assert.Equal("P1", result.PassageId);
        Assert.Single(session.Timeline);

        var link = result.Actions.OfType<RenderedLink>().Last();
        await session.FollowLinkAsync(link.Id);

        Assert.Equal(99L, session.Current.Variables["round"].AsInt());
    }

    [Fact]
    public async Task FollowLink_Onclick_ExecutedBeforeNavigation()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: '${nextPsg}'
              snapshot: true
              onclick:
              - type: 'assign'
                var: 'nextPsg'
                expr: '"P2"'
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'arrived'
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        var result = await session.FollowLinkAsync(navId);

        Assert.Equal("P2", result.PassageId);
    }

    [Fact]
    public async Task FollowLink_AssignsBeforeLink_BundledIntoSameSnapshot()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'a'
              expr: '1'
            - type: 'assign'
              var: 'b'
              expr: '2'
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;
        var countBefore = session.Timeline.Count;

        await session.FollowLinkAsync(navId);

        Assert.Equal(countBefore + 1, session.Timeline.Count);
        Assert.Equal(1L, session.Timeline[^1].Variables["a"].AsInt());
        Assert.Equal(2L, session.Timeline[^1].Variables["b"].AsInt());
    }

    // ── Goto ─────────────────────────────────────────────────────────────────

    private static GameSession MakeGotoSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'goto'
              target: 'P2'
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'landed'
            """,
        ]);
        return new GameSession(module, masterSeed: 1);
    }

    [Fact]
    public void Goto_NavigatesWithoutSnapshot() =>
        Assert.Single(MakeGotoSession().Timeline);

    [Fact]
    public void Goto_RendersTarget() =>
        Assert.Equal("P2", MakeGotoSession().CurrentRender.PassageId);

    // ── Step back/forward ────────────────────────────────────────────────────

    [Fact]
    public async Task StepBack_DecrementsHistoryIndex()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        Assert.Equal(1, session.HistoryIndex);
    }

    [Fact]
    public async Task StepBack_ReRendersFromSnapshot()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        // At P3, round == 2 (set during P2's own render). Timeline[1] (pointing at P2) captures
        // state from BEFORE P2 rendered (round == 1); stepping back restores that and re-renders
        // P2, which re-applies its assign and reproduces "Round 2" in the live rendered content.
        session.StepBack();
        Assert.Equal("P2", session.CurrentRender.PassageId);
        Assert.Equal(1L, session.Current.Variables["round"].AsInt());
        Assert.Contains(session.CurrentRender.Nodes, n => n is RenderedText t && t.Value == "Round 2");
    }

    [Fact]
    public async Task StepBack_IsRewound_True()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        Assert.True(session.IsRewound);
    }

    [Fact]
    public async Task StepForward_IncrementsHistoryIndex()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        session.StepForward();
        Assert.Equal(2, session.HistoryIndex);
    }

    [Fact]
    public void CannotStepBackPastGameStart()
    {
        var (session, _) = MakeSimpleSession();
        Assert.Throws<InvalidOperationException>(() => session.StepBack());
    }

    [Fact]
    public async Task CannotStepForwardAtHead()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        Assert.Throws<InvalidOperationException>(() => session.StepForward());
    }

    // ── Resume from here ─────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeFromHere_TruncatesFutureTimeline()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        var countBefore = session.Timeline.Count;

        session.ResumeFromHere();

        Assert.True(session.Timeline.Count < countBefore);
    }

    [Fact]
    public async Task ResumeFromHere_IsRewound_False()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        session.ResumeFromHere();
        Assert.False(session.IsRewound);
    }

    [Fact]
    public async Task ResumeFromHere_NewLinkCreatesNewSnapshot()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        session.ResumeFromHere();
        var nav = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        var countBefore = session.Timeline.Count;

        await session.FollowLinkAsync(nav.Id);

        Assert.Equal(countBefore + 1, session.Timeline.Count);
    }

    // ── Jump to present ──────────────────────────────────────────────────────

    [Fact]
    public async Task JumpToPresent_MovesToTimelineHead()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        session.StepBack();

        session.JumpToPresent();

        Assert.Equal(session.Timeline.Count - 1, session.HistoryIndex);
    }

    [Fact]
    public async Task JumpToPresent_IsRewound_False()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();

        session.JumpToPresent();

        Assert.False(session.IsRewound);
    }

    [Fact]
    public async Task JumpToPresent_DoesNotTruncateTimeline()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.StepBack();
        session.StepBack();
        var countBefore = session.Timeline.Count;

        session.JumpToPresent();

        Assert.Equal(countBefore, session.Timeline.Count);
    }

    [Fact]
    public void JumpToPresent_WhileLive_IsNoOp()
    {
        var (session, _) = MakeSimpleSession();
        var result = session.JumpToPresent();
        Assert.Equal(session.CurrentRender.PassageId, result.PassageId);
        Assert.Equal(0, session.HistoryIndex);
    }

    // ── Checkpoint ───────────────────────────────────────────────────────────

    private static GameSession MakeCheckpointSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'before'
            - type: 'checkpoint'
              id: 'cp1'
              display: 'Round 1 Complete'
              diagnostic: 'round_1_done'
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        return new GameSession(module, masterSeed: 1);
    }

    [Fact]
    public void CheckpointNode_CreatesCheckpointSnapshot() =>
        Assert.Contains(MakeCheckpointSession().Timeline, s => s.Kind == SnapshotKind.Checkpoint);

    [Fact]
    public void CheckpointNode_DisplayLabel_Preserved()
    {
        var cp = MakeCheckpointSession().Timeline.Single(s => s.Kind == SnapshotKind.Checkpoint);
        Assert.Equal("Round 1 Complete", cp.DisplayLabel);
    }

    [Fact]
    public void CheckpointNode_DiagnosticLabel_Preserved()
    {
        var cp = MakeCheckpointSession().Timeline.Single(s => s.Kind == SnapshotKind.Checkpoint);
        Assert.Equal("round_1_done", cp.DiagnosticLabel);
    }

    [Fact]
    public void CheckpointNode_InMiddleOfPassage_CapturesCurrentState()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '1'
            - type: 'checkpoint'
              id: 'cp1'
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var cp = session.Timeline.Single(s => s.Kind == SnapshotKind.Checkpoint);
        Assert.Equal(1L, cp.Variables["round"].AsInt());
    }

    // ── Input ────────────────────────────────────────────────────────────────

    [Fact]
    public void Input_EmitsRenderedInput()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        Assert.Equal("Enter name", input.Label);
    }

    [Fact]
    public async Task FollowLink_CommitsInputDraftToVar()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, "Alice");

        await session.FollowLinkAsync(link.Id);

        Assert.Equal("Alice", session.Current.Variables["playerName"].AsString());
    }

    [Fact]
    public async Task FollowLink_WithInputCommit_CreatesChoiceSnapshot()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, "Alice");

        await session.FollowLinkAsync(link.Id);

        Assert.Equal(SnapshotKind.Choice, session.Current.Kind);
    }

    [Fact]
    public async Task FollowLink_WithInputCommit_NavigatesToTarget()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, "Alice");

        var result = await session.FollowLinkAsync(link.Id);

        Assert.Equal("P2", result.PassageId);
    }

    [Fact]
    public async Task FollowLink_WithInvalidInput_Throws()
    {
        var session = MakeInputSession();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.FollowLinkAsync(link.Id));
    }

    [Fact]
    public async Task StepBackPastInput_ResetsInputVar()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, "Alice");
        await session.FollowLinkAsync(link.Id);

        session.StepBack();

        Assert.DoesNotContain("playerName", session.Current.Variables.Keys);
    }

    [Fact]
    public async Task ResumeFromHereAtPreInput_AllowsResubmit()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, "Alice");
        await session.FollowLinkAsync(link.Id);
        session.StepBack();
        session.ResumeFromHere();

        var input2 = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link2 = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input2.Id, "Bob");
        await session.FollowLinkAsync(link2.Id);

        Assert.Equal("Bob", session.Current.Variables["playerName"].AsString());
    }

    [Fact]
    public async Task StepBackToInput_ReadsFromSnapshot_DoesNotReevaluate()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, "Alice");
        await session.FollowLinkAsync(link.Id);

        session.StepBack();
        session.StepForward();

        Assert.Equal("Alice", session.Current.Variables["playerName"].AsString());
        Assert.Equal("P2", session.CurrentRender.PassageId);
    }

    // ── View state ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ViewState_ResetOnStepBack()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        session.ViewState.ExpandedPopups.Add("something");
        session.StepBack();
        Assert.Empty(session.ViewState.ExpandedPopups);
    }

    // ── Popup transaction ────────────────────────────────────────────────────

    [Fact]
    public async Task PopupAccept_StateAffecting_DisplayLabel_DefaultsToDestinationPassageTitle()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: true
              target: 'P2'
              okay: 'Continue'
              content: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            title: 'The Second Passage'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Equal("The Second Passage", session.Current.DisplayLabel);
    }

    // Regression: reproduces the crash reported against Gen1CreepyYes.mws.yaml — a popup target
    // computed from a ternary chain (extracted from Cradle's own ternary passage-name assignment)
    // used to fail to parse at all, since the expression language had no ternary operator.
    [Fact]
    public async Task PopupAccept_ComputedTernaryTarget_ResolvesToMatchingBranch()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '2'
            - type: 'popup'
              snapshot: true
              target: '${round == 1 ? "Fever1" : round == 2 ? "Fever2" : "Fever3"}'
              okay: 'Continue'
              content: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Fever1'
            layout: 'narration'
            nodes: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Fever2'
            layout: 'narration'
            nodes: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Fever3'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        var result = await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Equal("Fever2", result.PassageId);
    }

    [Fact]
    public async Task PopupAccept_TrailingAssignAfterPopupInSamePassage_SurvivesPopupCommit()
    {
        // Regression: A Time of War's AdvancedWeaponryIntro — a top-level popup (target: Martial1)
        // followed by several top-level `assign` nodes (sepinc1, sepinc2, ...) later in the SAME
        // passage's own node list. Those assigns run during the passage's OWN render, directly
        // against the live store, well before the player ever sees the popup — they're not part of
        // the popup's own content, so they never touch its sandbox. Confirmed via a real save file:
        // sepinc1 was still empty in the very next timeline snapshot after AdvancedWeaponryIntro,
        // even though its own assign had already executed during that same render. Root cause:
        // ClosePopupAsync used to commit the popup's sandbox via a wholesale replace (RestoreSession)
        // — and that sandbox was cloned BEFORE the trailing assigns ran, so accepting the popup
        // silently reverted them. See VariableStore.CommitChangesTo's own remarks for the fix.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: true
              target: 'P2'
              okay: 'Continue'
              content: []
            - type: 'assign'
              var: 'sepinc1'
              expr: '"Gained a Servant"'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Equal("Gained a Servant", session.Current.Variables["sepinc1"].AsString());
    }

    [Fact]
    public async Task PopupAccept_NestedPopup_CommitsAssignFromOuterPopupsOwnContent()
    {
        // Regression: A Time of War's RumorD2 — `assign rumor2 = "visited"` sits in the OUTER
        // (`layout: reveal`) popup's own content, ahead of a nested `layout: setup` popup that's
        // the player's only way to actually leave (the outer has no `okay` of its own — see
        // docs/mws-format-latest.md §6's nested-popup pattern). Only the INNER popup ever gets
        // ClosePopupAsync'd (found one level into the outer's own Actions, per FindAction's own
        // remarks). Before the VariableStore.Clone() fix, the inner popup's sandbox baseline was
        // taken from the OUTER sandbox's state AFTER rumor2 already flipped to "visited" — so that
        // change was already "baked in" as the inner sandbox's own starting point, invisible to a
        // same-level before/after diff. rumor2 never reached the live store; the once-per-game
        // RumorD2 rumor kept reappearing every time RumorD was revisited instead.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              label: 'Click to reveal...'
              layout: 'reveal'
              cancel: 'Close'
              content:
              - type: 'text'
                value: 'A secret is revealed.'
              - type: 'assign'
                var: 'rumor2'
                expr: '"visited"'
              - type: 'popup'
                layout: 'setup'
                label: 'Click when finished reading...'
                target: 'P2'
                okay: 'Close'
                snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var outerPopup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();
        var innerPopup = Assert.Single(outerPopup.Actions.OfType<RenderedPopup>());

        await session.ClosePopupAsync(innerPopup.Id, accept: true);

        Assert.Equal("visited", session.Current.Variables["rumor2"].AsString());
    }

    [Fact]
    public async Task PopupAccept_TimelineLabel_OverridesDestinationPassageTitle()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: 'popup label'
              target: 'P2'
              okay: 'Continue'
              content: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            title: 'The Second Passage'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Equal("popup label", session.Current.DisplayLabel);
    }

    [Fact]
    public async Task PopupAccept_PreemptingGotoTimelineLabel_OverridesPopupTimelineLabel()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              snapshot: 'popup label'
              target: 'P2'
              okay: 'Continue'
              content: []
              onclose:
              - type: 'goto'
                target: 'P3'
                snapshot: 'goto label'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P3'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        var result = await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Equal("P3", result.PassageId);
        Assert.Equal("goto label", session.Current.DisplayLabel);
    }

    [Fact]
    public void Popup_ContentEvaluatedEagerly_NotYetCommittedToLiveStore()
    {
        var session = MakePopupSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        // Available immediately — no engine call needed to "open" the popup.
        Assert.Contains(popup.Content, n => n is RenderedText t && t.Value == "popup body");
        // Not yet committed to the live store: the GameStart snapshot predates P1's own render
        // (so it may not have "round" at all yet), but it must not show the popup's pending value.
        Assert.False(session.Current.Variables.TryGetValue("round", out var v) && v.AsInt() == 99);
    }

    [Fact]
    public async Task Popup_CloseCommitsStateAndNavigates()
    {
        var session = MakePopupSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        var result = await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Equal("P2", result.PassageId);
        Assert.Equal(99L, session.Current.Variables["round"].AsInt());
    }

    [Fact]
    public async Task Popup_CancelDiscardsStateAndDoesNotNavigate()
    {
        var session = MakePopupSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        var result = await session.ClosePopupAsync(popup.Id, accept: false);

        Assert.Equal("P1", result.PassageId);
        Assert.False(session.Current.Variables.TryGetValue("round", out var v) && v.AsInt() == 99);
    }

    [Fact]
    public async Task PopupAccept_NoTargetOrOnclose_ClosesWithoutReRenderingPassage()
    {
        // A popup with no target/onclose has nothing to navigate to — Okay should still commit its
        // sandboxed state (the 'round' assign below) to the live store, but must not re-render the
        // current passage: doing so would re-run its whole node list for no reason, and — if this
        // popup were guarded by a conditional the way Cradle's "setup" popups are — could even
        // re-trigger the guard and show the same popup again. The committed state isn't reflected
        // in session.Current.Variables until the *next* snapshot (that's an existing, separate
        // property — see Popup_ContentEvaluatedEagerly_NotYetCommittedToLiveStore) — so this proves
        // the merge happened by following a link afterward and checking the new snapshot instead.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              layout: 'setup'
              content:
              - type: 'assign'
                var: 'round'
                expr: '99'
              okay: 'Accept'
            - type: 'link'
              label: 'Continue'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var beforeRender = session.CurrentRender;
        var popup = beforeRender.Actions.OfType<RenderedPopup>().Single();
        session.ViewState.ExpandedPopups.Add(popup.Id);

        var result = await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Same(beforeRender, result);
        Assert.Equal("P1", result.PassageId);
        Assert.Single(session.Timeline);
        Assert.DoesNotContain(popup.Id, session.ViewState.ExpandedPopups);

        var link = result.Actions.OfType<RenderedLink>().Single();
        await session.FollowLinkAsync(link.Id);

        Assert.Equal(99L, session.Current.Variables["round"].AsInt());
    }

    // ── Popup-content input (action lookup, validity, Okay/Cancel) ────────────

    [Fact]
    public async Task PopupContentInput_IsReachableViaClosePopup()
    {
        // Regression test for the popup-content action-lookup bug: FindAction<T> must search a
        // popup's own actions, not just the passage-level ones, since popup content (including any
        // input nodes it contains) is rendered against its own sandboxed action-ID space.
        var session = MakePopupWithInputSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();
        var input = Assert.Single(popup.Actions.OfType<RenderedInput>());
        session.UpdateInputDraft(input.Id, "5");

        var result = await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.Equal("P2", result.PassageId);
        Assert.Equal(5L, session.Current.Variables["score"].AsInt());
    }

    [Fact]
    public async Task PopupOkay_WithoutInputDraft_Throws()
    {
        var session = MakePopupWithInputSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.ClosePopupAsync(popup.Id, accept: true));
    }

    [Fact]
    public async Task PopupCancel_DoesNotRequireValidInput()
    {
        var session = MakePopupWithInputSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        var result = await session.ClosePopupAsync(popup.Id, accept: false);

        Assert.Equal("P1", result.PassageId);
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("10", true)]
    [InlineData("-1", false)]
    [InlineData("11", false)]
    [InlineData("not a number", false)]
    [InlineData(null, false)]
    public void IsInputValid_EnforcesMinMax(string? draft, bool expectedValid)
    {
        var session = MakePopupWithInputSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();
        var input = Assert.Single(popup.Actions.OfType<RenderedInput>());
        if (draft is not null)
        {
            session.UpdateInputDraft(input.Id, draft);
        }

        Assert.Equal(expectedValid, session.IsInputValid(input));
    }

    [Fact]
    public void AreCurrentInputsValid_FalseUntilInputFilled_TrueAfter()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();

        Assert.False(session.AreCurrentInputsValid());

        session.UpdateInputDraft(input.Id, "Alice");

        Assert.True(session.AreCurrentInputsValid());
    }

    [Fact]
    public void FollowLink_DerivesNumberInputType_FromDeclaredVariableType()
    {
        var session = MakePopupWithInputSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();
        var input = Assert.Single(popup.Actions.OfType<RenderedInput>());

        Assert.Equal(InputValueType.Number, input.InputType);
    }

    [Fact]
    public void FollowLink_DerivesBooleanInputType_FromDeclaredVariableType()
    {
        var session = MakeBooleanInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();

        Assert.Equal(InputValueType.Boolean, input.InputType);
    }

    [Fact]
    public void IsInputValid_BooleanInput_ValidWithNoDraftAtAll()
    {
        // A boolean field has no "empty" state — unchecked/false is itself a valid value, so it
        // must never block the enclosing link, whether or not the player has touched it.
        var session = MakeBooleanInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();

        Assert.True(session.IsInputValid(input));
        Assert.True(session.AreCurrentInputsValid());
    }

    [Fact]
    public async Task FollowLink_BooleanInput_NoDraft_CommitsFalse()
    {
        var session = MakeBooleanInputSession();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        await session.FollowLinkAsync(link.Id);

        Assert.False(session.Current.Variables["completedMasterwork"].AsBool());
    }

    [Fact]
    public async Task FollowLink_BooleanInput_CheckedDraft_CommitsTrue()
    {
        var session = MakeBooleanInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        session.UpdateInputDraft(input.Id, true);

        await session.FollowLinkAsync(link.Id);

        Assert.True(session.Current.Variables["completedMasterwork"].AsBool());
    }

    // ── Game over (app::gameover) ────────────────────────────────────────────

    private static GameSession MakeGameOverLinkSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'round'
              expr: '1'
            - type: 'link'
              label: 'Finish'
              snapshot: true
              target: 'app::gameover'
            """,
        ]);
        return new GameSession(module, masterSeed: 1);
    }

    private static GameSession MakeGameOverPopupSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'popup'
              label: 'Open'
              snapshot: true
              target: 'app::gameover'
              okay: 'Close'
              content:
              - type: 'assign'
                var: 'round'
                expr: '99'
              - type: 'text'
                value: 'ending popup body'
            """,
        ],
        variablesYaml: """
            standard_variables: []
            variables:
              round:
                type: 'int'
                default: 0
            """);
        return new GameSession(module, masterSeed: 1);
    }

    [Fact]
    public void GameOverLinkSession_IsGameOverRequested_InitiallyFalse()
    {
        var session = MakeGameOverLinkSession();
        Assert.False(session.IsGameOverRequested);
    }

    [Fact]
    public async Task FollowLink_TargetIsAppGameOver_SetsIsGameOverRequested()
    {
        var session = MakeGameOverLinkSession();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        await session.FollowLinkAsync(link.Id);

        Assert.True(session.IsGameOverRequested);
    }

    [Fact]
    public async Task FollowLink_TargetIsAppGameOver_DoesNotNavigate()
    {
        var session = MakeGameOverLinkSession();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        var result = await session.FollowLinkAsync(link.Id);

        Assert.Equal("P1", result.PassageId);
    }

    [Fact]
    public async Task ClosePopup_TargetIsAppGameOver_SetsIsGameOverRequestedAndCommitsSandbox()
    {
        // The popup's own committed state (its `round` assign) must still apply even though there's
        // nowhere to navigate — only the navigation itself is replaced by the gameover signal, not
        // the transaction's state commit (see ClosePopupAsync's own remarks: popup.Sandbox.
        // CommitChangesTo runs unconditionally, before the gameover target check). Not directly observable via
        // session.Current.Variables here, since that reflects the last-pushed *snapshot* and no new
        // one is pushed for a gameover target (there's no destination passage to snapshot toward) —
        // checking the popup's own sandbox is the closest available proxy; a future "preserve
        // playthrough memory" implementation reading final state at gameover time will need its own
        // way to observe the post-commit store, not yet designed (see the TBD note on IsGameOverRequested).
        var session = MakeGameOverPopupSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        var result = await session.ClosePopupAsync(popup.Id, accept: true);

        Assert.True(session.IsGameOverRequested);
        Assert.Equal("P1", result.PassageId);
        Assert.Equal(99L, popup.Sandbox.GetVariable("round").AsInt());
    }

    // ── Missing passage target (PassageNotFoundException / recovery) ─────────

    private static GameSession MakeMissingTargetSession()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Bar is {bar}'
            - type: 'link'
              label: 'Go'
              target: 'Missing'
              onclick:
              - type: 'assign'
                var: 'bar'
                expr: '99'
            """,
        ],
        variablesYaml: """
            standard_variables: []
            variables:
              bar:
                type: 'int'
                default: 0
            """);
        return new GameSession(module, masterSeed: 1);
    }

    [Fact]
    public async Task FollowLink_TargetPassageDoesNotExist_ThrowsPassageNotFoundExceptionNamingTarget()
    {
        var session = MakeMissingTargetSession();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        var ex = await Assert.ThrowsAsync<PassageNotFoundException>(() => session.FollowLinkAsync(link.Id));

        Assert.Equal("Missing", ex.PassageId);
    }

    [Fact]
    public async Task RecoverFromFailedNavigation_AfterFailedFollowLink_RollsBackLeakedOnclickEffectsAndStaysOnCurrentPassage()
    {
        // FollowLinkAsync runs the link's onclick (mutating the live store) before it attempts to
        // render the destination — a PassageNotFoundException there leaves that mutation applied
        // to the live store even though no new timeline entry was pushed. RecoverFromFailedNavigation
        // should roll the store back to the last committed snapshot (bar's default, 0) and
        // re-render the passage the player is actually still on, not leave the leaked bar=99 in place.
        var session = MakeMissingTargetSession();
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();
        await Assert.ThrowsAsync<PassageNotFoundException>(() => session.FollowLinkAsync(link.Id));

        var result = session.RecoverFromFailedNavigation();

        Assert.Equal("P1", result.PassageId);
        Assert.Contains(result.Nodes, n => n is RenderedText t && t.Value == "Bar is 0");
    }

    // ── goto's own snapshot override (per-branch state-affecting) ────────────

    [Fact]
    public async Task FollowLink_GotoSnapshotTrue_ForcesSnapshotEvenWhenEnclosingLinkIsNot()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              onclick:
              - type: 'goto'
                target: 'P2'
                snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        await session.FollowLinkAsync(link.Id);

        Assert.Equal(2, session.Timeline.Count);
        Assert.Equal(1, session.HistoryIndex);
        Assert.Equal("P2", session.CurrentRender.PassageId);
    }

    [Fact]
    public async Task FollowLink_GotoSnapshotFalse_SuppressesSnapshotEvenWhenEnclosingLinkIsStateAffecting()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              snapshot: true
              onclick:
              - type: 'goto'
                target: 'P2'
                snapshot: false
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        await session.FollowLinkAsync(link.Id);

        Assert.Single(session.Timeline);
        Assert.Equal(0, session.HistoryIndex);
        Assert.Equal("P2", session.CurrentRender.PassageId);
    }

    [Fact]
    public async Task FollowLink_GotoWithoutSnapshotField_InheritsEnclosingLinkStateAffecting()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              snapshot: true
              onclick:
              - type: 'goto'
                target: 'P2'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var link = session.CurrentRender.Actions.OfType<RenderedLink>().Single();

        await session.FollowLinkAsync(link.Id);

        Assert.Equal(2, session.Timeline.Count);
    }

    // ── Live-edge active state (non-state-affecting link/goto chains) ────────

    private static async Task<(GameSession session, LoadedModule module)> MakeActiveStateSessionAsync()
    {
        // Start --(snapshot:true)--> Anchor --(no snapshot)--> Mid --(no snapshot)--> Final. Mirrors
        // a score-entry -> tie-break-round-1 -> tie-break-round-2 chain: only entering Anchor gets a
        // timeline entry; Mid/Final are reached via RenderInPlace and never bookmarked.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'Start'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'Anchor'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Anchor'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'anchor text'
            - type: 'link'
              label: 'ToMid'
              target: 'Mid'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Mid'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'mid text'
            - type: 'link'
              label: 'ToFinal'
              target: 'Final'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Final'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'final text'
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id);
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id);
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id);
        return (session, module);
    }

    [Fact]
    public async Task ActiveState_AfterChainOfInPlaceTransitions_CurrentRenderShowsFinalPassageWithNoNewTimelineEntries()
    {
        var (session, _) = await MakeActiveStateSessionAsync();

        Assert.Equal("Final", session.CurrentRender.PassageId);
        Assert.Equal(2, session.Timeline.Count); // Start + Anchor only
    }

    [Fact]
    public async Task ActiveState_StepBackOnce_ShowsAnchorWithoutConsumingATimelineEntry()
    {
        var (session, _) = await MakeActiveStateSessionAsync();

        var result = session.StepBack();

        Assert.Equal("Anchor", result.PassageId);
        Assert.Equal(1, session.HistoryIndex);
    }

    [Fact]
    public async Task ActiveState_StepBackThenStepForward_RestoresFinalWithoutReplayingMid()
    {
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack();

        var result = session.StepForward();

        Assert.Equal("Final", result.PassageId);
    }

    [Fact]
    public async Task ActiveState_StepBackThenJumpToPresent_RestoresFinal()
    {
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack();

        var result = session.JumpToPresent();

        Assert.Equal("Final", result.PassageId);
    }

    [Fact]
    public async Task ActiveState_StepBackTwice_ReachesStartWithoutDiscardingActiveState()
    {
        // Per the user's explicit correction: stepping back any amount must NOT discard the active
        // state — only ResumeFromHere (or a new real snapshot superseding it) does that.
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack(); // reveal Anchor's own bare render
        var result = session.StepBack(); // real decrement past Anchor

        Assert.Equal("Start", result.PassageId);
        Assert.Equal(0, session.HistoryIndex);
    }

    [Fact]
    public async Task ActiveState_StepBackPastAnchorThenForward_ArrivingAtLiveEdgeRestoresFinal()
    {
        // Only one real entry (Anchor) separates Start from the live edge here, so a single
        // StepForward from Start already arrives at the live edge and reveals Final directly — see
        // the deeper-chain test below for the case where an intermediate real entry is visited on
        // its own first.
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack(); // Final (active state) -> Anchor's own anchor
        session.StepBack(); // Anchor -> Start (real decrement, active state preserved)

        var result = session.StepForward(); // Start -> Anchor, which IS the live edge -> shows Final

        Assert.Equal("Final", result.PassageId);
    }

    [Fact]
    public async Task ActiveState_StepBackTwoRealEntriesPastAnchor_ForwardVisitsIntermediateEntryBeforeRestoringFinal()
    {
        // A longer chain: Start -> A -> Anchor (three real entries) -> Mid -> Final (active state,
        // never bookmarked). Stepping back three times (un-drift, then two real decrements) lands
        // on Start, two real indices before the live edge — forward must visit A on its own first,
        // only revealing the active state once it actually arrives at the live edge (Anchor).
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'Start'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'A'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'A'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'Anchor'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Anchor'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'ToMid'
              target: 'Mid'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Mid'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'ToFinal'
              target: 'Final'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Final'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id); // Start -> A
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id); // A -> Anchor
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id); // Anchor -> Mid (active state)
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id); // Mid -> Final (active state)

        session.StepBack(); // Final -> Anchor's own anchor
        session.StepBack(); // Anchor -> A (real decrement, active state preserved)
        session.StepBack(); // A -> Start (real decrement, active state still preserved)

        var afterFirstForward = session.StepForward(); // Start -> A (an ordinary real entry, not the live edge)
        Assert.Equal("A", afterFirstForward.PassageId);

        var afterSecondForward = session.StepForward(); // A -> Anchor, which IS the live edge -> shows Final
        Assert.Equal("Final", afterSecondForward.PassageId);
    }

    [Fact]
    public async Task ActiveState_StepBackPastAnchorThenJumpToPresent_RestoresFinal()
    {
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack();
        session.StepBack();

        var result = session.JumpToPresent();

        Assert.Equal("Final", result.PassageId);
    }

    [Fact]
    public async Task ActiveState_ResumeFromHere_DiscardsActiveStateAndPreventsForward()
    {
        // The one place an active state is deliberately thrown away: choosing to branch play from
        // a historical point makes whatever was ahead of it no longer applicable.
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack(); // reveal Anchor's own bare render

        session.ResumeFromHere();

        Assert.False(session.CanStepForward);
    }

    [Fact]
    public async Task ActiveState_NewStateAffectingLinkFromLiveEdge_DiscardsPendingActiveState()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'Start'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'Go'
              target: 'Anchor'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Anchor'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'ToMid'
              target: 'Mid'
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Mid'
            layout: 'narration'
            nodes:
            - type: 'link'
              label: 'ToFinal'
              target: 'Final'
              snapshot: true
            """,
            """
            format: 'mws/0.4'
            passage_id: 'Final'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id); // Start -> Anchor
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id); // Anchor -> Mid (active state)
        await session.FollowLinkAsync(session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id); // Mid -> Final (real snapshot, discards active state)

        Assert.Equal("Final", session.CurrentRender.PassageId);
        Assert.Equal(3, session.Timeline.Count);

        var result = session.StepBack();

        Assert.Equal("Anchor", result.PassageId); // not "Mid" — the stale active state was discarded
    }

    [Fact]
    public async Task ActiveState_Initially_IsRewoundIsFalse()
    {
        var (session, _) = await MakeActiveStateSessionAsync();
        Assert.False(session.IsRewound);
    }

    [Fact]
    public async Task ActiveState_AfterStepBack_IsRewoundIsTrue()
    {
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack();
        Assert.True(session.IsRewound);
    }

    [Fact]
    public async Task ActiveState_Initially_HasActiveStateTrueAndIsAtActiveStateTrue()
    {
        // Sitting at the live edge showing Final (the pending active state) without ever having
        // stepped back — a timeline UI should show a "current" entry beyond the last real snapshot
        // (Anchor) and highlight that entry, not Anchor's own.
        var (session, _) = await MakeActiveStateSessionAsync();
        Assert.True(session.HasActiveState);
        Assert.True(session.IsAtActiveState);
    }

    [Fact]
    public async Task ActiveState_AfterStepBackToAnchor_HasActiveStateTrueButIsAtActiveStateFalse()
    {
        // StepBack from the live edge reveals the anchor's own bare render (see StepBack's remarks)
        // — the pending active state still exists (a timeline UI keeps showing its "current" entry
        // in the list) but is no longer what's currently selected/highlighted.
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack();
        Assert.True(session.HasActiveState);
        Assert.False(session.IsAtActiveState);
    }

    [Fact]
    public async Task ActiveState_AfterStepBackIntoRealHistory_HasActiveStateTrueButIsAtActiveStateFalse()
    {
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack(); // reveals the anchor
        session.StepBack(); // steps into real history (Start)
        Assert.True(session.HasActiveState);
        Assert.False(session.IsAtActiveState);
    }

    [Fact]
    public async Task ActiveState_AfterResumeFromHere_HasActiveStateFalse()
    {
        var (session, _) = await MakeActiveStateSessionAsync();
        session.StepBack();
        session.ResumeFromHere();
        Assert.False(session.HasActiveState);
        Assert.False(session.IsAtActiveState);
    }

    [Fact]
    public void NoActiveState_HasActiveStateFalseAndIsAtActiveStateFalse()
    {
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.4'
            passage_id: 'Start'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'hello'
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);

        Assert.False(session.HasActiveState);
        Assert.False(session.IsAtActiveState);
    }

    // ── Save/restore ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Serialize_ProducesJson()
    {
        var (session, _) = await MakeThreeStepSessionAsync();
        var save = session.Serialize();

        var json = JsonSerializer.Serialize(save);
        Assert.False(string.IsNullOrWhiteSpace(json));

        var roundTripped = JsonSerializer.Deserialize<SessionSave>(json);
        Assert.NotNull(roundTripped);
        Assert.Equal(save.HistoryIndex, roundTripped!.HistoryIndex);
        Assert.Equal(save.Timeline.Count, roundTripped.Timeline.Count);
    }

    [Fact]
    public async Task Serialize_WritesSnapshotKindByName()
    {
        // SnapshotKind is [JsonConverter(typeof(JsonStringEnumConverter))] specifically so future
        // enum member additions/removals can't silently shift what an old save's stored value
        // means (see SnapshotKind's remarks) — pin the on-the-wire shape so a regression back to
        // plain-int serialization is caught here rather than as a corrupted save in the field.
        var (session, _) = await MakeThreeStepSessionAsync();
        var json = JsonSerializer.Serialize(session.Serialize());

        Assert.Contains("\"Kind\":\"GameStart\"", json);
        Assert.Contains("\"Kind\":\"Choice\"", json);
    }

    [Fact]
    public void Deserialize_PreV04IntBasedSnapshotKind_SilentlyReinterpreted()
    {
        // Pins a real gap, not a fix: a pre-MWS-v0.4 autosave's SnapshotKind was a plain ordinal
        // (GameStart=0, Choice=1, InputReceived=2, Checkpoint=3). Removing InputReceived shifted
        // Checkpoint from 3 to 2. JsonStringEnumConverter only restricts *writing* to strings — on
        // *read* it still accepts a raw number and casts it straight to the enum with no bounds
        // check, so a stale Kind:3 does NOT throw; it deserializes as an out-of-range SnapshotKind
        // value with no name. This test exists so a future "let's just add strict validation"
        // attempt has a concrete case to verify against, not because the gap is closed.
        var staleJson = """
            {"MasterSeed":1,"HistoryIndex":0,"Timeline":[{"PassageId":"P1","Kind":3,"Variables":{},"SeedOccurrences":{}}]}
            """;

        var save = JsonSerializer.Deserialize<SessionSave>(staleJson);

        Assert.NotNull(save);
        Assert.False(Enum.IsDefined(save!.Timeline[0].Kind));
    }

    [Fact]
    public async Task Restore_MatchesOriginalTimeline()
    {
        var (session, module) = await MakeThreeStepSessionAsync();
        var save = session.Serialize();

        var restored = GameSession.Restore(module, save);

        Assert.Equal(session.Timeline.Count, restored.Timeline.Count);
        Assert.Equal(session.HistoryIndex, restored.HistoryIndex);
    }

    [Fact]
    public async Task Restore_VariableStoreMatchesCurrentSnapshot()
    {
        var (session, module) = await MakeThreeStepSessionAsync();
        var save = session.Serialize();

        var restored = GameSession.Restore(module, save);

        Assert.Equal(2L, restored.Current.Variables["round"].AsInt());
    }

    [Fact]
    public async Task Restore_RendersCurrentPassageFromSnapshot()
    {
        var (session, module) = await MakeThreeStepSessionAsync();
        var save = session.Serialize();

        var restored = GameSession.Restore(module, save);

        Assert.Equal(session.CurrentRender.PassageId, restored.CurrentRender.PassageId);
    }

    [Fact]
    public async Task FollowLinkAsync_TargetPassageRenderThrows_SessionStaysUsable()
    {
        // Regression: PushAndRender used to commit the new Timeline entry and advance HistoryIndex
        // *before* calling RenderChainFrom (which can throw — e.g. a malformed expression, real
        // occurrence: S5Fate2.mws.yaml). When it threw, _cachedRenders never got its matching
        // entry appended, leaving it one shorter than Timeline/HistoryIndex — every subsequent
        // CurrentRender access (from ANY component re-rendering) then threw
        // ArgumentOutOfRangeException, permanently bricking the session. PushAndRender now renders
        // before committing, so a failed navigation leaves the session exactly as it was.
        var module = new ModuleLoader().LoadFromSources(
        [
            """
            format: 'mws/0.3'
            passage_id: 'P1'
            tags:
            - 'Begins-Here'
            layout: 'narration'
            nodes:
            - type: 'text'
              value: 'Welcome'
            - type: 'link'
              label: 'Go'
              target: 'P2'
              snapshot: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes:
            - type: 'assign'
              var: 'broken'
              expr: '{a} + {b}'
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedLink>().Single().Id;

        await Assert.ThrowsAnyAsync<Exception>(() => session.FollowLinkAsync(navId));

        // Must not throw — the failed navigation left Timeline/HistoryIndex/_cachedRenders in sync.
        var stillCurrent = session.CurrentRender;
        Assert.Equal("P1", stillCurrent.PassageId);
        Assert.Single(session.Timeline);
    }

    // ── Live-edge active state persistence (save/resume mid-chain) ────────────

    [Fact]
    public async Task Serialize_WithPendingActiveState_CapturesIt()
    {
        var (session, _) = await MakeActiveStateSessionAsync();

        var save = session.Serialize();

        Assert.NotNull(save.ActiveState);
        Assert.Equal("Final", save.ActiveState!.PassageId);
    }

    [Fact]
    public async Task Serialize_NoPendingActiveState_ActiveStateIsNull()
    {
        var (session, _) = await MakeThreeStepSessionAsync();

        var save = session.Serialize();

        Assert.Null(save.ActiveState);
    }

    [Fact]
    public async Task Restore_WithPendingActiveState_ResumesShowingIt()
    {
        // A save taken mid-tie-break (or any other chain of non-state-affecting transitions) must
        // not silently lose that progress back to the bare anchor on resume — see ActiveState's own
        // remarks on why _cachedRenders alone (never persisted) isn't enough to reconstruct this.
        var (session, module) = await MakeActiveStateSessionAsync();
        var save = session.Serialize();

        var restored = GameSession.Restore(module, save);

        Assert.Equal("Final", restored.CurrentRender.PassageId);
        Assert.Equal(2, restored.Timeline.Count);
    }

    [Fact]
    public async Task Restore_WithPendingActiveState_StepBackStillShowsAnchorFirst()
    {
        var (session, module) = await MakeActiveStateSessionAsync();
        var save = session.Serialize();
        var restored = GameSession.Restore(module, save);

        var result = restored.StepBack();

        Assert.Equal("Anchor", result.PassageId);
        Assert.Equal(1, restored.HistoryIndex);
    }

    [Fact]
    public async Task Serialize_JsonRoundTrip_PreservesActiveState()
    {
        var (session, module) = await MakeActiveStateSessionAsync();
        var json = JsonSerializer.Serialize(session.Serialize());

        var roundTripped = JsonSerializer.Deserialize<SessionSave>(json);
        Assert.NotNull(roundTripped?.ActiveState);

        var restored = GameSession.Restore(module, roundTripped!);
        Assert.Equal("Final", restored.CurrentRender.PassageId);
    }
}
