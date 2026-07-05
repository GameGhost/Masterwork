using Masterwork.ModuleFormat;
using Masterwork.Engine.Expressions;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.Engine;

/// <summary>
/// The main entry point for playing a loaded module. The engine is pull-based: the App calls
/// <see cref="FollowLinkAsync"/> / <see cref="SubmitInputAsync"/> / <see cref="OpenPopupAsync"/> /
/// <see cref="ClosePopupAsync"/> and gets back typed render results. Timeline state is the only
/// durable state; passage-scoped let variables and <see cref="SessionViewState"/> are transient
/// and never persisted.
/// </summary>
public sealed class GameSession
{
    private readonly LoadedModule _module;
    private readonly long _masterSeed;
    private readonly VariableStore _store;
    private readonly SessionPrng _prng;
    private readonly IPassageRenderer _passageRenderer;
    private readonly IExpressionEvaluator _expressionEvaluator;
    private readonly ILogger<GameSession> _logger;
    private readonly List<SessionSnapshot> _timeline = [];
    private readonly List<PassageRenderResult> _cachedRenders = [];
    private HashSet<string> _visitedPassageIds = [];
    private PendingPopup? _pendingPopup;

    private sealed record PendingPopup(string ActionId, VariableStore Sandbox, string? OnClose, bool StateAffecting);

    /// <summary>The full history of snapshots recorded so far, including any rewound-past future.</summary>
    public IReadOnlyList<SessionSnapshot> Timeline => _timeline;

    /// <summary>Index of the current position within <see cref="Timeline"/>.</summary>
    public int HistoryIndex { get; private set; }

    /// <summary>The snapshot at <see cref="HistoryIndex"/>.</summary>
    public SessionSnapshot Current => _timeline[HistoryIndex];

    /// <summary>The cached render corresponding to <see cref="Current"/>.</summary>
    public PassageRenderResult CurrentRender => _cachedRenders[HistoryIndex];

    /// <summary>True when <see cref="HistoryIndex"/> is not at the end of <see cref="Timeline"/> (i.e. the player has stepped back).</summary>
    public bool IsRewound => HistoryIndex < _timeline.Count - 1;

    /// <summary>True when <see cref="StepBack"/> can be called.</summary>
    public bool CanStepBack => HistoryIndex > 0;

    /// <summary>True when <see cref="StepForward"/> can be called.</summary>
    public bool CanStepForward => IsRewound;

    /// <summary>Transient, non-persisted UI state for the current position.</summary>
    public SessionViewState ViewState { get; } = new();

    /// <summary>
    /// Starts a new session at the module's start passage (or <paramref name="startPassageIdOverride"/> if given).
    /// </summary>
    /// <param name="module">The loaded module to play.</param>
    /// <param name="masterSeed">Seed for all deterministic randomness in this session — see <see cref="SessionPrng"/>.</param>
    /// <param name="standardVars">Initial values for standard variables (e.g. player names), applied before the first render.</param>
    /// <param name="startPassageIdOverride">Overrides the module's <c>Begins-Here</c> passage as the starting point, mainly for tests.</param>
    /// <param name="passageRenderer">Renderer dependency, e.g. for testing with mocks. Defaults to a new <see cref="PassageRenderer"/> if omitted.</param>
    /// <param name="expressionEvaluator">Evaluator dependency, e.g. for testing with mocks. Defaults to a new <see cref="ExpressionEvaluator"/> if omitted.</param>
    /// <param name="logger">Logger dependency. Defaults to discarding log output if omitted.</param>
    /// <exception cref="InvalidOperationException">No override was given and the module has no <c>Begins-Here</c> passage.</exception>
    public GameSession(LoadedModule module, long masterSeed,
        IReadOnlyDictionary<string, StoryValue>? standardVars = null, string? startPassageIdOverride = null,
        IPassageRenderer? passageRenderer = null, IExpressionEvaluator? expressionEvaluator = null,
        ILogger<GameSession>? logger = null)
    {
        _module = module;
        _masterSeed = masterSeed;
        _logger = logger ?? NullLogger<GameSession>.Instance;
        _expressionEvaluator = expressionEvaluator ?? new ExpressionEvaluator();
        _passageRenderer = passageRenderer ?? new PassageRenderer(_expressionEvaluator);
        _prng = new SessionPrng(masterSeed);
        _store = new VariableStore(module.Variables, _prng, _expressionEvaluator);
        if (standardVars is not null)
        {
            foreach (var (k, v) in standardVars)
            {
                _store.SetSessionVariable(k, v);
            }
        }

        var startId = startPassageIdOverride ?? module.StartPassageId
            ?? throw new InvalidOperationException("No start passage specified and the module has no 'Begins-Here' passage.");

        _logger.LogDebug("Starting session at passage '{StartPassageId}' with master seed {MasterSeed}", startId, masterSeed);
        PushAndRender(startId, SnapshotKind.GameStart, submittedInput: null, displayLabel: null, diagnosticLabel: null);
    }

    // Restores a session from a save. Fully correct for ordinary snapshots (whose captured state
    // always precedes the passage they point to); Checkpoint snapshots capture mid-passage state,
    // so replaying them by re-rendering from scratch is a best-effort approximation, not exact.
    private GameSession(LoadedModule module, SessionSave save, IPassageRenderer? passageRenderer,
        IExpressionEvaluator? expressionEvaluator, ILogger<GameSession>? logger)
    {
        _module = module;
        _masterSeed = save.MasterSeed;
        _logger = logger ?? NullLogger<GameSession>.Instance;
        _expressionEvaluator = expressionEvaluator ?? new ExpressionEvaluator();
        _passageRenderer = passageRenderer ?? new PassageRenderer(_expressionEvaluator);
        _prng = new SessionPrng(_masterSeed);
        _store = new VariableStore(module.Variables, _prng, _expressionEvaluator);
        ViewState = new SessionViewState();

        _logger.LogDebug("Restoring session: {SnapshotCount} snapshots, history index {HistoryIndex}", save.Timeline.Count, save.HistoryIndex);

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

    /// <summary>Reconstructs a session from a previously <see cref="Serialize"/>d save, without re-executing any player actions.</summary>
    /// <param name="module">The loaded module the save belongs to.</param>
    /// <param name="save">A prior <see cref="Serialize"/> capture.</param>
    /// <param name="passageRenderer">Renderer dependency, e.g. for testing with mocks. Defaults to a new <see cref="PassageRenderer"/> if omitted.</param>
    /// <param name="expressionEvaluator">Evaluator dependency, e.g. for testing with mocks. Defaults to a new <see cref="ExpressionEvaluator"/> if omitted.</param>
    /// <param name="logger">Logger dependency. Defaults to discarding log output if omitted.</param>
    public static GameSession Restore(LoadedModule module, SessionSave save,
        IPassageRenderer? passageRenderer = null, IExpressionEvaluator? expressionEvaluator = null,
        ILogger<GameSession>? logger = null) =>
        new(module, save, passageRenderer, expressionEvaluator, logger);

    /// <summary>Captures the full timeline and current position as a serializable save.</summary>
    public SessionSave Serialize() => new(_masterSeed, [.. _timeline], HistoryIndex);

    // ── Player actions ───────────────────────────────────────────────────────

    /// <summary>Follows a <see cref="RenderedNavigation"/> action: runs its <c>onclick</c> nodes, resolves its target, and navigates (creating a timeline snapshot if the navigation is state-affecting).</summary>
    public Task<PassageRenderResult> FollowLinkAsync(string actionId)
    {
        _logger.LogDebug("Following link '{ActionId}'", actionId);
        var nav = FindAction<RenderedNavigation>(actionId);

        if (nav.OnClickRaw.Count > 0)
        {
            _passageRenderer.RenderNodeList(nav.OnClickRaw, _store, _module);
        }

        var targetId = ResolveTarget(nav.Target);

        var result = nav.StateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, submittedInput: null, nav.TimelineLabel, diagnosticLabel: null)
            : RenderInPlace(targetId);

        return Task.FromResult(result);
    }

    /// <summary>Stores the submitted value into a <see cref="RenderedInput"/> action's variable, creates an <see cref="SnapshotKind.InputReceived"/> snapshot, and navigates to its <c>onsubmit</c> target.</summary>
    public Task<PassageRenderResult> SubmitInputAsync(string actionId, object value)
    {
        _logger.LogDebug("Submitting input '{ActionId}'", actionId);
        var input = FindAction<RenderedInput>(actionId);
        var exprValue = input.InputType == InputValueType.Number
            ? StoryValue.Of(Convert.ToInt64(value))
            : StoryValue.Of(value.ToString() ?? "");
        _store.SetSessionVariable(input.Var, exprValue);

        var targetId = ResolveTarget(input.OnSubmit);
        var result = PushAndRender(targetId, SnapshotKind.InputReceived, exprValue, displayLabel: null, diagnosticLabel: null);
        return Task.FromResult(result);
    }

    /// <summary>
    /// Opens a <see cref="RenderedPopup"/> action. Evaluates the popup's content against a sandbox
    /// copy of the store — state changes stay pending until <see cref="ClosePopupAsync"/> commits
    /// them, per the popup transaction model.
    /// </summary>
    public Task<PopupRenderResult> OpenPopupAsync(string actionId)
    {
        _logger.LogDebug("Opening popup '{ActionId}'", actionId);
        var popup = FindAction<RenderedPopup>(actionId);
        ViewState.ExpandedPopups.Add(actionId);

        var sandbox = _store.Clone();
        var content = _passageRenderer.RenderNodeList(popup.RawContent, sandbox, _module);
        _pendingPopup = new PendingPopup(actionId, sandbox, popup.OnClose, popup.StateAffecting);

        return Task.FromResult(new PopupRenderResult(content));
    }

    /// <summary>Closes the currently open popup, committing its pending state changes and navigating to its <c>onclose</c> target as a single transaction.</summary>
    /// <exception cref="InvalidOperationException">No popup with <paramref name="actionId"/> is currently open.</exception>
    public Task<PassageRenderResult> ClosePopupAsync(string actionId)
    {
        _logger.LogDebug("Closing popup '{ActionId}'", actionId);
        if (_pendingPopup is not { } pending || pending.ActionId != actionId)
        {
            throw new InvalidOperationException($"Popup '{actionId}' is not open.");
        }

        _store.RestoreSession(pending.Sandbox.SessionSnapshot());
        _pendingPopup = null;
        ViewState.ExpandedPopups.Remove(actionId);

        var targetId = pending.OnClose is null ? Current.PassageId : ResolveTarget(pending.OnClose);

        var result = pending.StateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, submittedInput: null, displayLabel: null, diagnosticLabel: null)
            : RenderInPlace(targetId);
        return Task.FromResult(result);
    }

    /// <summary>Marks a private gate as confirmed in <see cref="ViewState"/>.</summary>
    public void ConfirmPrivateGate(string gateId) => ViewState.ConfirmedGates.Add(gateId);

    /// <summary>Records an in-progress (not yet submitted) input value in <see cref="ViewState"/>.</summary>
    public void UpdateInputDraft(string actionId, object draft) => ViewState.InputDrafts[actionId] = draft;

    // ── Timeline navigation ──────────────────────────────────────────────────

    /// <summary>Moves one step back in the timeline and re-renders from the restored snapshot.</summary>
    /// <exception cref="InvalidOperationException"><see cref="CanStepBack"/> is false.</exception>
    public PassageRenderResult StepBack()
    {
        if (!CanStepBack)
        {
            throw new InvalidOperationException("Cannot step back past the start of the timeline.");
        }

        HistoryIndex--;
        _logger.LogDebug("Stepped back to history index {HistoryIndex}", HistoryIndex);
        return RestoreAndRerenderCurrent();
    }

    /// <summary>Moves one step forward in the timeline and re-renders from the restored snapshot.</summary>
    /// <exception cref="InvalidOperationException"><see cref="CanStepForward"/> is false.</exception>
    public PassageRenderResult StepForward()
    {
        if (!CanStepForward)
        {
            throw new InvalidOperationException("Cannot step forward at the head of the timeline.");
        }

        HistoryIndex++;
        _logger.LogDebug("Stepped forward to history index {HistoryIndex}", HistoryIndex);
        return RestoreAndRerenderCurrent();
    }

    /// <summary>Discards any rewound future, unlocking live play again from the current position.</summary>
    public void ResumeFromHere()
    {
        _logger.LogDebug("Resuming from history index {HistoryIndex}, discarding rewound future", HistoryIndex);
        TruncateFuture();
        ViewState.Reset();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private PassageRenderResult PushAndRender(string passageId, SnapshotKind kind,
        StoryValue? submittedInput, string? displayLabel, string? diagnosticLabel)
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
        var result = _passageRenderer.Render(_module.Passages[passageId], _store, _module, _visitedPassageIds);
        while (result.PendingGoto is not null)
        {
            var nextId = result.PendingGoto;
            _visitedPassageIds.Add(nextId);
            result = _passageRenderer.Render(_module.Passages[nextId], _store, _module, _visitedPassageIds);
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
        if (_timeline.Count == 0)
        {
            return;
        }

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
        {
            return _cachedRenders[HistoryIndex];
        }

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
            ? _expressionEvaluator.Evaluate(raw[2..^1], _store).AsString()
            : raw;
}
