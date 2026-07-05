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
            - type: 'navigation'
              label: 'Go'
              target: 'P2'
              state_affecting: true
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
            - type: 'navigation'
              label: 'Go'
              target: 'P2'
              state_affecting: true
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
            - type: 'navigation'
              label: 'Go'
              target: 'P3'
              state_affecting: true
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
        var nav1 = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single().Id;
        await session.FollowLinkAsync(nav1);
        var nav2 = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single().Id;
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
              text: 'Type your name'
              input: 'string'
              var: 'playerName'
              onsubmit: 'P2'
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
              state_affecting: true
              onclose: 'P2'
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
        var navId = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single().Id;
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
            - type: 'navigation'
              label: 'Go'
              target: 'P2'
              state_affecting: false
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single().Id;
        var countBefore = session.Timeline.Count;

        await session.FollowLinkAsync(navId);

        Assert.Equal(countBefore, session.Timeline.Count);
    }

    [Fact]
    public async Task FollowLink_RendersNewPassage()
    {
        var (session, _) = MakeSimpleSession();
        var navId = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single().Id;
        var result = await session.FollowLinkAsync(navId);
        Assert.Equal("P2", result.PassageId);
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
            - type: 'navigation'
              label: 'Go'
              target: '${nextPsg}'
              state_affecting: true
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
        var navId = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single().Id;

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
            - type: 'navigation'
              label: 'Go'
              target: 'P2'
              state_affecting: true
            """,
            """
            format: 'mws/0.3'
            passage_id: 'P2'
            layout: 'narration'
            nodes: []
            """,
        ]);
        var session = new GameSession(module, masterSeed: 1);
        var navId = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single().Id;
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
        var nav = session.CurrentRender.Actions.OfType<RenderedNavigation>().Single();
        var countBefore = session.Timeline.Count;

        await session.FollowLinkAsync(nav.Id);

        Assert.Equal(countBefore + 1, session.Timeline.Count);
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
            - type: 'navigation'
              label: 'Go'
              target: 'P2'
              state_affecting: true
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
    public async Task SubmitInput_StoresValueInVar()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        await session.SubmitInputAsync(input.Id, "Alice");
        Assert.Equal("Alice", session.Current.Variables["playerName"].AsString());
    }

    [Fact]
    public async Task SubmitInput_CreatesInputReceivedSnapshot()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        await session.SubmitInputAsync(input.Id, "Alice");
        Assert.Equal(SnapshotKind.InputReceived, session.Current.Kind);
    }

    [Fact]
    public async Task SubmitInput_NavigatesToOnsubmit()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        var result = await session.SubmitInputAsync(input.Id, "Alice");
        Assert.Equal("P2", result.PassageId);
    }

    [Fact]
    public async Task StepBackPastInput_ResetsInputVar()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        await session.SubmitInputAsync(input.Id, "Alice");

        session.StepBack();

        Assert.DoesNotContain("playerName", session.Current.Variables.Keys);
    }

    [Fact]
    public async Task ResumeFromHereAtPreInput_AllowsResubmit()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        await session.SubmitInputAsync(input.Id, "Alice");
        session.StepBack();
        session.ResumeFromHere();

        var input2 = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        await session.SubmitInputAsync(input2.Id, "Bob");

        Assert.Equal("Bob", session.Current.Variables["playerName"].AsString());
    }

    [Fact]
    public async Task StepBackToInput_ReadsFromSnapshot_DoesNotReevaluate()
    {
        var session = MakeInputSession();
        var input = session.CurrentRender.Actions.OfType<RenderedInput>().Single();
        await session.SubmitInputAsync(input.Id, "Alice");

        session.StepBack();
        session.StepForward();

        Assert.Equal("Alice", session.Current.Variables["playerName"].AsString());
        Assert.Equal("P2", session.CurrentRender.PassageId);
    }

    // ── View state ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExpandPopup_SetInViewState()
    {
        var session = MakePopupSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();
        await session.OpenPopupAsync(popup.Id);
        Assert.Contains(popup.Id, session.ViewState.ExpandedPopups);
    }

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
    public async Task Popup_ContentEvaluatedOnOpen()
    {
        var session = MakePopupSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();

        var opened = await session.OpenPopupAsync(popup.Id);

        Assert.Contains(opened.Content, n => n is RenderedText t && t.Value == "popup body");
        // Not yet committed to the live store: the GameStart snapshot predates P1's own render
        // (so it may not have "round" at all yet), but it must not show the popup's pending value.
        Assert.False(session.Current.Variables.TryGetValue("round", out var v) && v.AsInt() == 99);
    }

    [Fact]
    public async Task Popup_CloseCommitsStateAndNavigates()
    {
        var session = MakePopupSession();
        var popup = session.CurrentRender.Actions.OfType<RenderedPopup>().Single();
        await session.OpenPopupAsync(popup.Id);

        var result = await session.ClosePopupAsync(popup.Id);

        Assert.Equal("P2", result.PassageId);
        Assert.Equal(99L, session.Current.Variables["round"].AsInt());
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
