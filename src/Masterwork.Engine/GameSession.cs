using Masterwork.ModuleFormat;

namespace Masterwork.Engine;

// The engine is pull-based: the App calls FollowLinkAsync / SubmitInputAsync / Open-ClosePopupAsync
// and gets back typed render results. Timeline state is the only durable state; passage-scoped let
// variables and SessionViewState are transient and never persisted.
public sealed class GameSession
{
    private readonly LoadedModule _module;
    private readonly long _masterSeed;
    private readonly VariableStore _store;
    private readonly SessionPrng _prng;
    private readonly List<SessionSnapshot> _timeline = [];
    private readonly List<PassageRenderResult> _cachedRenders = [];
    private HashSet<string> _visitedPassageIds = [];
    private PendingPopup? _pendingPopup;

    private sealed record PendingPopup(string ActionId, VariableStore Sandbox, string? OnClose, bool StateAffecting);

    public IReadOnlyList<SessionSnapshot> Timeline => _timeline;
    public int HistoryIndex { get; private set; }
    public SessionSnapshot Current => _timeline[HistoryIndex];
    public PassageRenderResult CurrentRender => _cachedRenders[HistoryIndex];
    public bool IsRewound => HistoryIndex < _timeline.Count - 1;
    public bool CanStepBack => HistoryIndex > 0;
    public bool CanStepForward => IsRewound;
    public SessionViewState ViewState { get; } = new();

    public GameSession(LoadedModule module, long masterSeed,
        IReadOnlyDictionary<string, ExprValue>? standardVars = null, string? startPassageIdOverride = null)
    {
        _module = module;
        _masterSeed = masterSeed;
        _prng = new SessionPrng(masterSeed);
        _store = new VariableStore(module.Variables, _prng);
        if (standardVars is not null)
            foreach (var (k, v) in standardVars) _store.SetSessionVariable(k, v);

        var startId = startPassageIdOverride ?? module.StartPassageId
            ?? throw new InvalidOperationException("No start passage specified and the module has no 'Begins-Here' passage.");

        PushAndRender(startId, SnapshotKind.GameStart, submittedInput: null, displayLabel: null, diagnosticLabel: null);
    }

    // Restores a session from a save. Fully correct for ordinary snapshots (whose captured state
    // always precedes the passage they point to); Checkpoint snapshots capture mid-passage state,
    // so replaying them by re-rendering from scratch is a best-effort approximation, not exact.
    private GameSession(LoadedModule module, SessionSave save)
    {
        _module = module;
        _masterSeed = save.MasterSeed;
        _prng = new SessionPrng(_masterSeed);
        _store = new VariableStore(module.Variables, _prng);
        ViewState = new SessionViewState();

        foreach (var snapshot in save.Timeline)
        {
            _store.RestoreSession(snapshot.Variables);
            _prng.RestoreOccurrences(snapshot.SeedOccurrences);
            _timeline.Add(snapshot);
            _visitedPassageIds.Add(snapshot.PassageId);
            _cachedRenders.Add(RenderChainFrom(snapshot.PassageId));
        }

        HistoryIndex = save.HistoryIndex;
        _store.RestoreSession(_timeline[HistoryIndex].Variables);
        _prng.RestoreOccurrences(_timeline[HistoryIndex].SeedOccurrences);
    }

    public static GameSession Restore(LoadedModule module, SessionSave save) => new(module, save);

    public SessionSave Serialize() => new(_masterSeed, [.. _timeline], HistoryIndex);

    // ── Player actions ───────────────────────────────────────────────────────

    public Task<PassageRenderResult> FollowLinkAsync(string actionId)
    {
        var nav = FindAction<RenderedNavigation>(actionId);

        if (nav.OnClickRaw.Count > 0)
            PassageRenderer.RenderNodeList(nav.OnClickRaw, _store, _module);

        var targetId = ResolveTarget(nav.Target);

        var result = nav.StateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, submittedInput: null, nav.TimelineLabel, diagnosticLabel: null)
            : RenderInPlace(targetId);

        return Task.FromResult(result);
    }

    public Task<PassageRenderResult> SubmitInputAsync(string actionId, object value)
    {
        var input = FindAction<RenderedInput>(actionId);
        var exprValue = input.InputType == InputValueType.Number
            ? ExprValue.Of(Convert.ToInt64(value))
            : ExprValue.Of(value.ToString() ?? "");
        _store.SetSessionVariable(input.Var, exprValue);

        var targetId = ResolveTarget(input.OnSubmit);
        var result = PushAndRender(targetId, SnapshotKind.InputReceived, exprValue, displayLabel: null, diagnosticLabel: null);
        return Task.FromResult(result);
    }

    // Evaluates the popup's content against a sandbox copy of the store — state changes stay
    // pending until ClosePopupAsync commits them, per the popup transaction model.
    public Task<PopupRenderResult> OpenPopupAsync(string actionId)
    {
        var popup = FindAction<RenderedPopup>(actionId);
        ViewState.ExpandedPopups.Add(actionId);

        var sandbox = _store.Clone();
        var content = PassageRenderer.RenderNodeList(popup.RawContent, sandbox, _module);
        _pendingPopup = new PendingPopup(actionId, sandbox, popup.OnClose, popup.StateAffecting);

        return Task.FromResult(new PopupRenderResult(content));
    }

    public Task<PassageRenderResult> ClosePopupAsync(string actionId)
    {
        if (_pendingPopup is not { } pending || pending.ActionId != actionId)
            throw new InvalidOperationException($"Popup '{actionId}' is not open.");

        _store.RestoreSession(pending.Sandbox.SessionSnapshot());
        _pendingPopup = null;
        ViewState.ExpandedPopups.Remove(actionId);

        var targetId = pending.OnClose is null ? Current.PassageId : ResolveTarget(pending.OnClose);

        var result = pending.StateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, submittedInput: null, displayLabel: null, diagnosticLabel: null)
            : RenderInPlace(targetId);
        return Task.FromResult(result);
    }

    public void ConfirmPrivateGate(string gateId) => ViewState.ConfirmedGates.Add(gateId);
    public void UpdateInputDraft(string actionId, object draft) => ViewState.InputDrafts[actionId] = draft;

    // ── Timeline navigation ──────────────────────────────────────────────────

    public PassageRenderResult StepBack()
    {
        if (!CanStepBack) throw new InvalidOperationException("Cannot step back past the start of the timeline.");
        HistoryIndex--;
        return RestoreAndRerenderCurrent();
    }

    public PassageRenderResult StepForward()
    {
        if (!CanStepForward) throw new InvalidOperationException("Cannot step forward at the head of the timeline.");
        HistoryIndex++;
        return RestoreAndRerenderCurrent();
    }

    // Discards any rewound future, unlocking live play again from the current position.
    public void ResumeFromHere()
    {
        TruncateFuture();
        ViewState.Reset();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private PassageRenderResult PushAndRender(string passageId, SnapshotKind kind,
        ExprValue? submittedInput, string? displayLabel, string? diagnosticLabel)
    {
        TruncateFuture();

        _timeline.Add(new SessionSnapshot
        {
            PassageId = passageId,
            Kind = kind,
            Variables = _store.SessionSnapshot(),
            SeedOccurrences = _prng.SnapshotOccurrences(),
            SubmittedInput = submittedInput,
            DisplayLabel = displayLabel,
            DiagnosticLabel = diagnosticLabel,
        });
        HistoryIndex = _timeline.Count - 1;
        RecomputeVisitedFromTimeline();

        var result = RenderChainFrom(passageId);
        _cachedRenders.Add(result);

        HandleCheckpoints(result);
        ViewState.Reset();
        _pendingPopup = null;
        return result;
    }

    // Renders `passageId` without creating a timeline entry (non-state-affecting navigation).
    // The current entry's cached render is overwritten in place so CurrentRender reflects it.
    private PassageRenderResult RenderInPlace(string passageId)
    {
        var result = RenderChainFrom(passageId);
        _cachedRenders[HistoryIndex] = result;
        RecomputeVisitedFromTimeline();
        HandleCheckpoints(result);
        ViewState.Reset();
        _pendingPopup = null;
        return result;
    }

    // Renders a passage, following any goto chain to its final landing passage.
    private PassageRenderResult RenderChainFrom(string passageId)
    {
        _visitedPassageIds.Add(passageId);
        var result = PassageRenderer.Render(_module.Passages[passageId], _store, _module, _visitedPassageIds);
        while (result.PendingGoto is not null)
        {
            var nextId = result.PendingGoto;
            _visitedPassageIds.Add(nextId);
            result = PassageRenderer.Render(_module.Passages[nextId], _store, _module, _visitedPassageIds);
        }
        return result;
    }

    // Checkpoint nodes become bookmark timeline entries pointing at the same render that produced
    // them (state as of the end of that render — see the SessionSnapshot doc comment on precision).
    private void HandleCheckpoints(PassageRenderResult result)
    {
        foreach (var cp in result.Checkpoints)
        {
            _timeline.Add(new SessionSnapshot
            {
                PassageId = result.PassageId,
                Kind = SnapshotKind.Checkpoint,
                Variables = _store.SessionSnapshot(),
                SeedOccurrences = _prng.SnapshotOccurrences(),
                SubmittedInput = null,
                DisplayLabel = cp.Display,
                DiagnosticLabel = cp.Diagnostic,
            });
            _cachedRenders.Add(result);
            HistoryIndex = _timeline.Count - 1;
        }
    }

    private void TruncateFuture()
    {
        if (_timeline.Count == 0) return;
        var keep = HistoryIndex + 1;
        if (_timeline.Count > keep)
        {
            _timeline.RemoveRange(keep, _timeline.Count - keep);
            _cachedRenders.RemoveRange(keep, _cachedRenders.Count - keep);
        }
    }

    // Restores variables/PRNG to `snapshot`, then re-renders its passage so the live store ends up
    // consistent with what's displayed (matches the invariant that a snapshot always precedes the
    // render of the passage it points to). Checkpoint snapshots are the one exception: they capture
    // mid-passage state, so a fresh top-down render would double-apply that passage's own earlier
    // assigns — for those, the originally-cached render is trusted as-is instead.
    private PassageRenderResult RestoreAndRerenderCurrent()
    {
        var snapshot = _timeline[HistoryIndex];
        _store.RestoreSession(snapshot.Variables);
        _prng.RestoreOccurrences(snapshot.SeedOccurrences);
        RecomputeVisitedFromTimeline();
        ViewState.Reset();
        _pendingPopup = null;

        if (snapshot.Kind == SnapshotKind.Checkpoint)
            return _cachedRenders[HistoryIndex];

        var result = RenderChainFrom(snapshot.PassageId);
        _cachedRenders[HistoryIndex] = result;
        return result;
    }

    private void RecomputeVisitedFromTimeline() =>
        _visitedPassageIds = [.. _timeline.Take(HistoryIndex + 1).Select(s => s.PassageId)];

    private T FindAction<T>(string actionId) where T : RenderedAction
    {
        var action = CurrentRender.Actions.FirstOrDefault(a => a.Id == actionId)
            ?? throw new InvalidOperationException($"No action '{actionId}' in the current passage render.");
        return action as T ?? throw new InvalidOperationException($"Action '{actionId}' is not a {typeof(T).Name}.");
    }

    private string ResolveTarget(string raw) =>
        raw.StartsWith("${", StringComparison.Ordinal) && raw.EndsWith('}')
            ? ExpressionEvaluator.Evaluate(raw[2..^1], _store).AsString()
            : raw;
}
