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
                snapshot_label: 'goto label'
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
                snapshot_label: 'goto label'
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
}
