using Masterwork.ModuleFormat;
using Masterwork.Engine.Expressions;
using Masterwork.Engine.Rendering;
using Masterwork.Engine.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.Engine;

/// <summary>
/// The main entry point for playing a loaded module. The engine is pull-based: the App calls
/// <see cref="FollowLinkAsync"/> / <see cref="ClosePopupAsync"/> and gets back typed render
/// results. Opening/closing a popup's display is not an engine concept at all — a
/// <see cref="Rendering.RenderedPopup"/> already carries its rendered content (see its remarks), so
/// showing one is a pure UI state toggle; only committing it via Accept goes through
/// <see cref="ClosePopupAsync"/>. Likewise <c>input</c> nodes have no submit action of their own —
/// their values are committed as part of whichever <see cref="FollowLinkAsync"/>/<see cref="ClosePopupAsync"/>
/// call follows them, via <see cref="UpdateInputDraft"/>-tracked drafts in <see cref="ViewState"/>.
/// Timeline state is the only durable state; passage-scoped let variables and
/// <see cref="SessionViewState"/> are transient and never persisted.
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
        PushAndRender(startId, SnapshotKind.GameStart, displayLabel: null, diagnosticLabel: null);
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

    /// <summary>
    /// Follows a <see cref="RenderedLink"/> action: commits every currently-showing <c>input</c>'s
    /// draft value to its bound variable, runs the link's <c>onclick</c> nodes, resolves its target
    /// (a <c>goto</c> among <c>onclick</c> preempts it), and navigates — all as a single snapshot
    /// when the link is state-affecting. The snapshot's timeline label is, in priority order: a
    /// preempting <c>goto</c>'s own <c>snapshot_label</c>; else the link's own <c>snapshot</c> label;
    /// else the destination passage's <c>title</c> (see <see cref="ResolvePassageTitle"/>). If neither
    /// a <c>goto</c> nor <see cref="RenderedLink.Target"/> resolves a destination, there's nothing to
    /// navigate to — the link's <c>onclick</c> effects have already run against the live store, but
    /// nothing else happens (mirrors <see cref="ClosePopupAsync"/>'s same no-destination case).
    /// </summary>
    /// <exception cref="InvalidOperationException">A currently-showing input doesn't have a valid value.</exception>
    public Task<PassageRenderResult> FollowLinkAsync(string actionId)
    {
        _logger.LogDebug("Following link '{ActionId}'", actionId);
        var link = FindAction<RenderedLink>(actionId);

        CommitInputs(CurrentRender.Actions, _store);

        string? pendingGoto = null;
        string? pendingGotoLabel = null;
        if (link.OnClickRaw.Count > 0)
        {
            var onClickResult = _passageRenderer.RenderNodeList(link.OnClickRaw, _store, _module);
            pendingGoto = onClickResult.PendingGoto;
            pendingGotoLabel = onClickResult.PendingGotoLabel;
        }

        if (pendingGoto is null && link.Target is null)
        {
            ViewState.Reset();
            return Task.FromResult(CurrentRender);
        }

        var targetId = pendingGoto ?? ResolveTarget(link.Target!);
        var displayLabel = (pendingGoto is not null ? pendingGotoLabel : null) ?? link.SnapshotLabel;

        var result = link.StateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, displayLabel, diagnosticLabel: null)
            : RenderInPlace(targetId);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Closes a <see cref="RenderedPopup"/> via its Okay/Cancel action. When <paramref name="accept"/>
    /// is <see langword="true"/> (Okay), commits its pending input drafts and state changes to the
    /// live store, runs <c>onclose</c> against them (a <c>goto</c> among <c>onclose</c> preempts
    /// <c>target</c>), and navigates — all as a single transaction. The snapshot's timeline label is,
    /// in priority order: a preempting <c>goto</c>'s own <c>snapshot_label</c>; else the popup's own
    /// <c>snapshot</c> label; else the destination passage's <c>title</c> (see
    /// <see cref="ResolvePassageTitle"/>). If neither a <c>goto</c> nor <c>target</c> resolves a
    /// destination, there's nothing to navigate to — the popup just closes in place (state already
    /// committed above) without re-rendering the current passage, since a re-render would re-run its
    /// whole node list for no reason. When <see langword="false"/> (Cancel), the popup's sandbox
    /// is discarded entirely: no commit, no <c>onclose</c>, no navigation — the caller doesn't even
    /// need to call this for Cancel, since nothing session-side needs to happen (see
    /// <see cref="RenderedPopup"/>'s remarks); it's provided for symmetry.
    /// </summary>
    /// <exception cref="InvalidOperationException">No popup with <paramref name="actionId"/> exists in <see cref="CurrentRender"/>, or (on Okay) a pending input doesn't have a valid value.</exception>
    public Task<PassageRenderResult> ClosePopupAsync(string actionId, bool accept)
    {
        _logger.LogDebug("Closing popup '{ActionId}' ({Outcome})", actionId, accept ? "okay" : "cancel");
        var popup = FindAction<RenderedPopup>(actionId);

        if (!accept)
        {
            return Task.FromResult(CurrentRender);
        }

        CommitInputs(popup.Actions, popup.Sandbox);

        string? pendingGoto = null;
        string? pendingGotoLabel = null;
        if (popup.OnCloseRaw.Count > 0)
        {
            var onCloseResult = _passageRenderer.RenderNodeList(popup.OnCloseRaw, popup.Sandbox, _module);
            pendingGoto = onCloseResult.PendingGoto;
            pendingGotoLabel = onCloseResult.PendingGotoLabel;
        }

        _store.RestoreSession(popup.Sandbox.SessionSnapshot());

        if (pendingGoto is null && popup.Target is null)
        {
            // Nothing to navigate to — Okay just closes the popup. Don't re-render the current
            // passage: that would re-run its whole node list, which can re-trigger guard
            // conditions (re-showing this same popup) or re-draw random values, for no reason
            // since nothing about the passage itself is meant to change here.
            ViewState.Reset();
            return Task.FromResult(CurrentRender);
        }

        var targetId = pendingGoto ?? ResolveTarget(popup.Target!);
        var displayLabel = (pendingGoto is not null ? pendingGotoLabel : null) ?? popup.SnapshotLabel;

        var result = popup.StateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, displayLabel, diagnosticLabel: null)
            : RenderInPlace(targetId);
        return Task.FromResult(result);
    }

    /// <summary>Whether <paramref name="input"/>'s current draft satisfies its implicit-required/min/max constraints.</summary>
    public bool IsInputValid(RenderedInput input)
    {
        if (!ViewState.InputDrafts.TryGetValue(input.Id, out var draft) || draft?.ToString() is not { Length: > 0 } text)
        {
            return false;
        }

        if (input.InputType != InputValueType.Number)
        {
            return true;
        }

        if (!long.TryParse(text, out var n))
        {
            return false;
        }

        return (input.Min is not { } min || n >= min) && (input.Max is not { } max || n <= max);
    }

    /// <summary>Whether every <see cref="RenderedInput"/> among <paramref name="actions"/> is valid — used to gate a passage's <c>link</c> buttons or a popup's Okay button.</summary>
    public bool AreInputsValid(IEnumerable<RenderedAction> actions) => actions.OfType<RenderedInput>().All(IsInputValid);

    /// <summary>Whether every <see cref="RenderedInput"/> in <see cref="CurrentRender"/> is valid.</summary>
    public bool AreCurrentInputsValid() => AreInputsValid(CurrentRender.Actions);

    // Commits every input among `actions` from its ViewState draft into `target`, per the "any link
    // acts as a submit for every currently-showing input" model. Throws rather than silently
    // skipping an invalid one — the App is expected to keep the triggering button disabled until
    // AreInputsValid is true, so reaching here with an invalid draft is a defensive invariant, not a
    // normal user-facing failure path.
    private void CommitInputs(IEnumerable<RenderedAction> actions, VariableStore target)
    {
        foreach (var input in actions.OfType<RenderedInput>())
        {
            if (!IsInputValid(input))
            {
                throw new InvalidOperationException($"Input '{input.Id}' does not have a valid value.");
            }

            var text = ViewState.InputDrafts[input.Id].ToString()!;
            var value = input.InputType == InputValueType.Number
                ? StoryValue.Of(long.Parse(text))
                : StoryValue.Of(text);
            target.SetSessionVariable(input.Var, value);
        }
    }

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

    /// <summary>Jumps straight back to the head of the timeline without discarding it — unlike <see cref="ResumeFromHere"/>, the rewound-past future (if any) is kept.</summary>
    public PassageRenderResult JumpToPresent()
    {
        if (!IsRewound)
        {
            return CurrentRender;
        }

        HistoryIndex = _timeline.Count - 1;
        _logger.LogDebug("Jumped to present at history index {HistoryIndex}", HistoryIndex);
        return RestoreAndRerenderCurrent();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private PassageRenderResult PushAndRender(string passageId, SnapshotKind kind,
        string? displayLabel, string? diagnosticLabel)
    {
        TruncateFuture();

        var snapshot = new SessionSnapshot
        {
            PassageId = passageId,
            Kind = kind,
            Variables = _store.SessionSnapshot(),
            SeedOccurrences = _prng.SnapshotOccurrences(),
            DisplayLabel = displayLabel ?? ResolvePassageTitle(passageId),
            DiagnosticLabel = diagnosticLabel,
        };

        // Render BEFORE committing the new timeline entry. RenderChainFrom can throw (e.g. a
        // malformed expression reachable from this passage) — _timeline, HistoryIndex, and
        // _cachedRenders must advance together or not at all, otherwise CurrentRender
        // (_cachedRenders[HistoryIndex]) permanently throws IndexOutOfRange on every subsequent
        // render of any component that reads it, bricking the session with no recovery short of a
        // full reload. Mirrors RenderInPlace's existing render-then-mutate ordering.
        var result = RenderChainFrom(passageId);

        _timeline.Add(snapshot);
        HistoryIndex = _timeline.Count - 1;
        RecomputeVisitedFromTimeline();
        _cachedRenders.Add(result);

        HandleCheckpoints(result);
        ViewState.Reset();
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
                DisplayLabel = cp.Display ?? ResolvePassageTitle(result.PassageId),
                DiagnosticLabel = cp.Diagnostic,
            });
            _cachedRenders.Add(result);
            HistoryIndex = _timeline.Count - 1;
        }
    }

    // The timeline scrubber's default label for any snapshot that doesn't specify its own
    // (link.snapshot/popup.snapshot's string form, checkpoint.display): the destination passage's
    // own title (plus subtitle, joined as "{title} - {subtitle}", when both are set), falling back
    // to its passage_id if the module doesn't set a title.
    private string ResolvePassageTitle(string passageId)
    {
        if (!_module.Passages.TryGetValue(passageId, out var doc) || doc.Title is null)
        {
            return passageId;
        }

        return doc.Subtitle is not null ? $"{doc.Title} - {doc.Subtitle}" : doc.Title;
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

    // Searches CurrentRender's own actions, plus one level of nesting into every popup's own
    // Actions (its content is rendered eagerly alongside the passage, so this is available
    // regardless of whether that popup is actually open in the UI right now — see RenderedPopup's
    // remarks).
    private T FindAction<T>(string actionId) where T : RenderedAction
    {
        var action = CurrentRender.Actions.FirstOrDefault(a => a.Id == actionId)
            ?? CurrentRender.Actions.OfType<RenderedPopup>().SelectMany(p => p.Actions).FirstOrDefault(a => a.Id == actionId)
            ?? throw new InvalidOperationException($"No action '{actionId}' in the current passage render.");
        return action as T ?? throw new InvalidOperationException($"Action '{actionId}' is not a {typeof(T).Name}.");
    }

    // "${module::entrypoint}" is a special sentinel, not an ordinary expression — it's how a shared
    // asset-pack onboarding flow's final goto/navigation reaches the loaded module's own Begins-Here
    // passage without hardcoding a passage id it can't know in advance (masterwork-plan-rev14.md Q24).
    private const string ModuleEntrypointTarget = "${module::entrypoint}";

    private string ResolveTarget(string raw) =>
        raw switch
        {
            ModuleEntrypointTarget => _module.StartPassageId
                ?? throw new InvalidOperationException("'module::entrypoint' was used as a target, but this module has no 'Begins-Here' passage."),
            _ when raw.StartsWith("${", StringComparison.Ordinal) && raw.EndsWith('}') =>
                _expressionEvaluator.Evaluate(raw[2..^1], _store).AsString(),
            _ => raw,
        };
}
