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

    // A non-state-affecting (RenderInPlace) navigation at the live edge diverges from
    // _timeline[^1]'s own anchor snapshot without ever getting a bookmark of its own — e.g. a
    // chain of automatic tie-break rounds. _activeState captures the most recent such divergence
    // (only ever one at a time — a later in-place transition overwrites it, per ActiveState's own
    // remarks) and survives however far back the player steps into real history — only
    // ResumeFromHere (branching play from a historical point) or a new PushAndRender (superseding
    // it with a real snapshot) discard it; see their own remarks. _viewingAnchor tracks, only while
    // sitting exactly at the live edge (HistoryIndex == _timeline.Count - 1), whether CurrentRender
    // currently shows the anchor's own bare render (via StepBack) instead of _activeState — reset
    // whenever the player leaves the live edge, so a later return to it defaults back to showing
    // _activeState fresh rather than re-requiring a second Forward press.
    private ActiveState? _activeState;
    private bool _viewingAnchor;

    /// <summary>The full history of snapshots recorded so far, including any rewound-past future.</summary>
    public IReadOnlyList<SessionSnapshot> Timeline => _timeline;

    /// <summary>Index of the current position within <see cref="Timeline"/>.</summary>
    public int HistoryIndex { get; private set; }

    /// <summary>The snapshot at <see cref="HistoryIndex"/>.</summary>
    public SessionSnapshot Current => _timeline[HistoryIndex];

    /// <summary>The cached render corresponding to <see cref="Current"/>.</summary>
    public PassageRenderResult CurrentRender => _cachedRenders[HistoryIndex];

    /// <summary>
    /// True when <see cref="HistoryIndex"/> is not at the end of <see cref="Timeline"/> (i.e. the
    /// player has stepped back), or when it's at the end but <see cref="StepBack"/> has shown the
    /// live edge's own anchor snapshot instead of a pending <see cref="ActiveState"/> (see the
    /// remarks on the fields above) — both are "reviewing something other than where play actually
    /// is right now," which is what disables further state-changing actions until the player
    /// explicitly steps/jumps back to the front or calls <see cref="ResumeFromHere"/>.
    /// </summary>
    public bool IsRewound => HistoryIndex < _timeline.Count - 1 || _viewingAnchor;

    /// <summary>True when <see cref="StepBack"/> can be called.</summary>
    public bool CanStepBack => HistoryIndex > 0 || (HistoryIndex == _timeline.Count - 1 && _activeState is not null && !_viewingAnchor);

    /// <summary>True when <see cref="StepForward"/> can be called.</summary>
    public bool CanStepForward => IsRewound;

    /// <summary>
    /// True when there's live-edge progress beyond the last real <see cref="Timeline"/> snapshot —
    /// a non-state-affecting navigation has diverged from <c>Timeline[^1]</c>'s own anchor without
    /// ever getting a bookmark of its own (see the remarks on <c>_activeState</c> above). A UI
    /// showing the timeline should render an extra "current" entry after the last real snapshot
    /// whenever this is true, distinct from that last snapshot itself.
    /// </summary>
    public bool HasActiveState => _activeState is not null;

    /// <summary>
    /// True while <see cref="CurrentRender"/> is showing that pending active state (see
    /// <see cref="HasActiveState"/>) rather than either a historical snapshot or the live edge's own
    /// anchor snapshot (shown instead via <see cref="StepBack"/>, see <c>_viewingAnchor</c>'s
    /// remarks) — i.e. whether a timeline UI's "current" pseudo-entry, if <see cref="HasActiveState"/>,
    /// is what's actually selected right now.
    /// </summary>
    public bool IsAtActiveState => HistoryIndex == _timeline.Count - 1 && _activeState is not null && !_viewingAnchor;

    /// <summary>Transient, non-persisted UI state for the current position.</summary>
    public SessionViewState ViewState { get; } = new();

    /// <summary>
    /// Set once a <c>link</c>/<c>popup</c>/<c>goto</c> resolves its target to the <c>"app::gameover"</c>
    /// sentinel (see <see cref="ResolveTarget"/>'s remarks) — the App is responsible for reacting to
    /// this (deleting the module's autosave, recording playthrough memory, and returning to the main
    /// menu; see <c>Play.razor</c>'s <c>HandleGameOverIfRequestedAsync</c>), not the engine. One-way:
    /// once true, this <see cref="GameSession"/> is expected to be torn down by the App, not reused.
    /// </summary>
    public bool IsGameOverRequested { get; private set; }

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

        // Active state pending at save time only means anything if it was taken at the live edge
        // (see ActiveState's remarks) — restore it the same way RenderInPlace originally produced
        // it, so resuming lands the player back where they actually left off instead of on the bare
        // anchor. _viewingAnchor defaults to false (showing the active state), matching "resuming
        // shows you what you were looking at when you saved."
        if (save.ActiveState is not null && HistoryIndex == _timeline.Count - 1)
        {
            _activeState = save.ActiveState;
            _store.RestoreSession(_activeState.Variables);
            _prng.RestoreOccurrences(_activeState.SeedOccurrences);
            _cachedRenders[HistoryIndex] = RenderChainFrom(_activeState.PassageId);
            RecomputeVisitedFromTimeline();
        }
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
    public SessionSave Serialize() => new(_masterSeed, [.. _timeline], HistoryIndex, _activeState);

    // ── Player actions ───────────────────────────────────────────────────────

    /// <summary>
    /// Follows a <see cref="RenderedLink"/> action: commits every currently-showing <c>input</c>'s
    /// draft value to its bound variable, runs the link's <c>onclick</c> nodes, resolves its target
    /// (a <c>goto</c> among <c>onclick</c> preempts it), and navigates. Whether this creates a new
    /// timeline snapshot is, in priority order: a preempting <c>goto</c>'s own <c>snapshot</c> (if
    /// explicitly set); else the link's own <c>snapshot</c>. The timeline label follows the same
    /// priority (the <c>goto</c>'s own label, if any, else the link's). A non-state-affecting
    /// navigation still lands on the destination passage (see <see cref="RenderInPlace"/>) — it just
    /// doesn't bookmark it, which <see cref="StepBack"/>/<see cref="StepForward"/>/<see cref="JumpToPresent"/>
    /// account for (see their own remarks). If neither a <c>goto</c> nor <see cref="RenderedLink.Target"/>
    /// resolves a destination, there's nothing to navigate to — the link's <c>onclick</c> effects
    /// have already run against the live store, but nothing else happens (mirrors
    /// <see cref="ClosePopupAsync"/>'s same no-destination case). If the resolved target is the
    /// <c>"app::gameover"</c> sentinel, this sets <see cref="IsGameOverRequested"/> and returns
    /// <see cref="CurrentRender"/> unchanged instead of navigating.
    /// </summary>
    /// <exception cref="InvalidOperationException">A currently-showing input doesn't have a valid value.</exception>
    public Task<PassageRenderResult> FollowLinkAsync(string actionId)
    {
        _logger.LogDebug("Following link '{ActionId}'", actionId);
        var link = FindAction<RenderedLink>(actionId);

        CommitInputs(CurrentRender.Actions, _store);

        string? pendingGoto = null;
        string? pendingGotoLabel = null;
        bool? pendingGotoStateAffecting = null;
        if (link.OnClickRaw.Count > 0)
        {
            var onClickResult = _passageRenderer.RenderNodeList(link.OnClickRaw, _store, _module);
            pendingGoto = onClickResult.PendingGoto;
            pendingGotoLabel = onClickResult.PendingGotoLabel;
            pendingGotoStateAffecting = onClickResult.PendingGotoStateAffecting;
        }

        if (pendingGoto is null && link.Target is null)
        {
            ViewState.Reset();
            return Task.FromResult(CurrentRender);
        }

        var targetId = pendingGoto ?? ResolveTarget(link.Target!);
        if (targetId == AppGameOverTarget)
        {
            IsGameOverRequested = true;
            ViewState.Reset();
            return Task.FromResult(CurrentRender);
        }

        var displayLabel = (pendingGoto is not null ? pendingGotoLabel : null) ?? link.SnapshotLabel;
        var stateAffecting = (pendingGoto is not null ? pendingGotoStateAffecting : null) ?? link.StateAffecting;

        var result = stateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, displayLabel, diagnosticLabel: null)
            : RenderInPlace(targetId);

        return Task.FromResult(result);
    }

    /// <summary>
    /// Closes a <see cref="RenderedPopup"/> via its Okay/Cancel action. When <paramref name="accept"/>
    /// is <see langword="true"/> (Okay), commits its pending input drafts and state changes to the
    /// live store, runs <c>onclose</c> against them (a <c>goto</c> among <c>onclose</c> preempts
    /// <c>target</c>), and navigates — all as a single transaction. Whether this creates a new
    /// timeline snapshot, and its label, follow the same priority as <see cref="FollowLinkAsync"/>'s
    /// own remarks (a preempting <c>goto</c>'s own <c>snapshot</c>, if explicitly set, else the
    /// popup's own). If neither a <c>goto</c> nor <c>target</c> resolves a
    /// destination, there's nothing to navigate to — the popup just closes in place (state already
    /// committed above) without re-rendering the current passage, since a re-render would re-run its
    /// whole node list for no reason. If the resolved target is the <c>"app::gameover"</c> sentinel,
    /// this sets <see cref="IsGameOverRequested"/> and returns <see cref="CurrentRender"/> unchanged
    /// instead — the committed state above (including any <c>record</c>/achievement nodes in
    /// <c>onclose</c>) still applies. When <see langword="false"/> (Cancel), the popup's sandbox
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
        bool? pendingGotoStateAffecting = null;
        if (popup.OnCloseRaw.Count > 0)
        {
            var onCloseResult = _passageRenderer.RenderNodeList(popup.OnCloseRaw, popup.Sandbox, _module);
            pendingGoto = onCloseResult.PendingGoto;
            pendingGotoLabel = onCloseResult.PendingGotoLabel;
            pendingGotoStateAffecting = onCloseResult.PendingGotoStateAffecting;
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
        if (targetId == AppGameOverTarget)
        {
            IsGameOverRequested = true;
            ViewState.Reset();
            return Task.FromResult(CurrentRender);
        }

        var displayLabel = (pendingGoto is not null ? pendingGotoLabel : null) ?? popup.SnapshotLabel;
        var stateAffecting = (pendingGoto is not null ? pendingGotoStateAffecting : null) ?? popup.StateAffecting;

        var result = stateAffecting
            ? PushAndRender(targetId, SnapshotKind.Choice, displayLabel, diagnosticLabel: null)
            : RenderInPlace(targetId);
        return Task.FromResult(result);
    }

    /// <summary>Whether <paramref name="input"/>'s current draft satisfies its implicit-required/min/max constraints.</summary>
    public bool IsInputValid(RenderedInput input)
    {
        // A boolean field has no "empty" state to require — unchecked/false is itself a valid
        // value, so it never blocks the enclosing link/popup okay button, whether or not the
        // player has touched it yet.
        if (input.InputType == InputValueType.Boolean)
        {
            return true;
        }

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

            StoryValue value;
            if (input.InputType == InputValueType.Boolean)
            {
                // No draft at all (never touched) defaults to false — see IsInputValid's own
                // note on booleans having no "empty" state.
                var isChecked = ViewState.InputDrafts.TryGetValue(input.Id, out var boolDraft) && boolDraft is true;
                value = StoryValue.Of(isChecked);
            }
            else
            {
                var text = ViewState.InputDrafts[input.Id].ToString()!;
                value = input.InputType == InputValueType.Number
                    ? StoryValue.Of(long.Parse(text))
                    : StoryValue.Of(text);
            }

            target.SetSessionVariable(input.Var, value);
        }
    }

    /// <summary>Records an in-progress (not yet submitted) input value in <see cref="ViewState"/>.</summary>
    public void UpdateInputDraft(string actionId, object draft) => ViewState.InputDrafts[actionId] = draft;

    // ── Timeline navigation ──────────────────────────────────────────────────

    /// <summary>
    /// Moves one step back. If the live edge currently shows a pending <see cref="ActiveState"/> (a
    /// chain of non-state-affecting transitions since the last true snapshot — e.g. tie-break
    /// rounds), the first call shows that snapshot's own anchor render instead of consuming a real
    /// timeline entry, so "true snapshots" are never skipped over. <see cref="StepForward"/> or
    /// <see cref="JumpToPresent"/> restores the active state again without replaying whatever
    /// produced it. Further calls step through real timeline entries exactly as before — the active
    /// state is <em>not</em> discarded no matter how far back the player goes; only
    /// <see cref="ResumeFromHere"/> (choosing to branch play from a historical point) or a new
    /// state-affecting navigation (which supersedes it with a real snapshot — see
    /// <see cref="PushAndRender"/>) does that.
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CanStepBack"/> is false.</exception>
    public PassageRenderResult StepBack()
    {
        if (_activeState is not null && HistoryIndex == _timeline.Count - 1 && !_viewingAnchor)
        {
            _viewingAnchor = true;
            _logger.LogDebug("Stepping back: showing the anchor snapshot instead of the active state, at history index {HistoryIndex}", HistoryIndex);
            return RestoreAndRerenderCurrent();
        }

        if (!CanStepBack)
        {
            throw new InvalidOperationException("Cannot step back past the start of the timeline.");
        }

        _viewingAnchor = false;
        HistoryIndex--;
        _logger.LogDebug("Stepped back to history index {HistoryIndex}", HistoryIndex);
        return RestoreAndRerenderCurrent();
    }

    /// <summary>
    /// Moves one step forward. If the live edge's own anchor is currently showing because
    /// <see cref="StepBack"/> just revealed it, this restores the pending <see cref="ActiveState"/>
    /// directly — landing back on the state that was actually left, without replaying whatever
    /// non-state-affecting transitions produced it. Otherwise moves to the next real timeline
    /// entry as before; if that lands exactly on the live edge and an active state is pending
    /// there (reachable after stepping back through several real entries — see
    /// <see cref="StepBack"/>'s own remarks on it surviving that), the active state is shown
    /// instead of the bare anchor (arriving at the live edge always means "where play actually
    /// is," never an intermediate stop).
    /// </summary>
    /// <exception cref="InvalidOperationException"><see cref="CanStepForward"/> is false.</exception>
    public PassageRenderResult StepForward()
    {
        if (!CanStepForward)
        {
            throw new InvalidOperationException("Cannot step forward at the head of the timeline.");
        }

        if (_viewingAnchor && HistoryIndex == _timeline.Count - 1)
        {
            _viewingAnchor = false;
            _logger.LogDebug("Stepping forward: restoring the active state at history index {HistoryIndex}", HistoryIndex);
            return RestoreActiveState();
        }

        HistoryIndex++;
        _logger.LogDebug("Stepped forward to history index {HistoryIndex}", HistoryIndex);
        var result = RestoreAndRerenderCurrent();
        if (HistoryIndex == _timeline.Count - 1 && _activeState is not null)
        {
            _viewingAnchor = false;
            return RestoreActiveState();
        }
        return result;
    }

    /// <summary>
    /// Discards any rewound future and the pending active state (if any), unlocking live play
    /// again from the current position — the one place a pending <see cref="ActiveState"/> is
    /// deliberately thrown away, since choosing to branch play from a historical point makes
    /// whatever was ahead of it no longer applicable.
    /// </summary>
    public void ResumeFromHere()
    {
        _logger.LogDebug("Resuming from history index {HistoryIndex}, discarding rewound future and any pending active state", HistoryIndex);
        TruncateFuture();
        _activeState = null;
        _viewingAnchor = false;
        ViewState.Reset();
    }

    /// <summary>
    /// Jumps straight to the head of the timeline without discarding it — unlike
    /// <see cref="ResumeFromHere"/>, the rewound-past future (if any) is kept, and so is a pending
    /// <see cref="ActiveState"/>. Lands on that active state if one exists, same as
    /// <see cref="StepForward"/>'s own live-edge-arrival behavior, rather than stopping on the bare
    /// anchor.
    /// </summary>
    public PassageRenderResult JumpToPresent()
    {
        if (_viewingAnchor && HistoryIndex == _timeline.Count - 1)
        {
            _viewingAnchor = false;
            _logger.LogDebug("Returning to present: restoring the active state at history index {HistoryIndex}", HistoryIndex);
            return RestoreActiveState();
        }

        if (!IsRewound)
        {
            return CurrentRender;
        }

        HistoryIndex = _timeline.Count - 1;
        _viewingAnchor = false;
        _logger.LogDebug("Jumped to present at history index {HistoryIndex}", HistoryIndex);
        var result = RestoreAndRerenderCurrent();
        return _activeState is not null ? RestoreActiveState() : result;
    }

    /// <summary>
    /// Rolls the live variable store and PRNG back to the last successfully-committed timeline
    /// entry and re-renders it — recovery for a <see cref="FollowLinkAsync"/>/<see cref="ClosePopupAsync"/>
    /// call that threw partway through (e.g. a <see cref="PassageNotFoundException"/> for a target
    /// that doesn't exist). Both of those calls commit input drafts and onclick/onclose side
    /// effects to the live store <em>before</em> attempting to render the destination, so a
    /// failure there can leave the store ahead of what <see cref="Current"/>/<see cref="CurrentRender"/>
    /// still reflect (neither of which moves until the destination renders successfully); this
    /// discards that partial progress and re-renders the passage the player was already on. Safe
    /// to call even when nothing actually failed — it just recomputes the same state that's
    /// already current.
    /// </summary>
    public PassageRenderResult RecoverFromFailedNavigation()
    {
        _logger.LogWarning("Recovering from a failed navigation at history index {HistoryIndex}.", HistoryIndex);
        // A failed FollowLinkAsync/ClosePopupAsync never reaches RenderInPlace/PushAndRender (see
        // their own remarks on rendering before committing), so an active state that was already
        // pending and showing *before* the failed attempt is untouched by it — recover back to
        // that if there is one, not the bare anchor, since that's what the player actually saw. The
        // HistoryIndex check guards against a failure reachable while reviewing older real history
        // (bypassing the UI's usual disabling) — an active state pending at the live edge doesn't
        // apply there.
        return _activeState is not null && !_viewingAnchor && HistoryIndex == _timeline.Count - 1
            ? RestoreActiveState()
            : RestoreAndRerenderCurrent();
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private PassageRenderResult PushAndRender(string passageId, SnapshotKind kind,
        string? displayLabel, string? diagnosticLabel)
    {
        TruncateFuture();
        // A new real snapshot supersedes any pending active state — it was "the state before this
        // passage rendered" relative to the OLD live edge, which this call is moving past. Unlike
        // StepBack, this isn't "reviewing" the active state, it's continuing past it — see
        // ActiveState's and StepBack's own remarks on why only this and ResumeFromHere discard it.
        _activeState = null;
        _viewingAnchor = false;

        // Variables/SeedOccurrences must be captured pre-render (see SessionSnapshot's own
        // remarks); DisplayLabel is deliberately deferred until after render below — it needs
        // result.Title, which ResolveDisplayLabel reads instead of re-deriving from the module's
        // raw, unexpanded title text (see that method's own remarks).
        var variables = _store.SessionSnapshot();
        var seedOccurrences = _prng.SnapshotOccurrences();

        // Render BEFORE committing the new timeline entry. RenderChainFrom can throw (e.g. a
        // malformed expression reachable from this passage) — _timeline, HistoryIndex, and
        // _cachedRenders must advance together or not at all, otherwise CurrentRender
        // (_cachedRenders[HistoryIndex]) permanently throws IndexOutOfRange on every subsequent
        // render of any component that reads it, bricking the session with no recovery short of a
        // full reload. Mirrors RenderInPlace's existing render-then-mutate ordering.
        var result = RenderChainFrom(passageId);

        var snapshot = new SessionSnapshot
        {
            PassageId = passageId,
            Kind = kind,
            Variables = variables,
            SeedOccurrences = seedOccurrences,
            DisplayLabel = displayLabel ?? ResolveDisplayLabel(result),
            DiagnosticLabel = diagnosticLabel,
        };

        _timeline.Add(snapshot);
        HistoryIndex = _timeline.Count - 1;
        RecomputeVisitedFromTimeline();
        _cachedRenders.Add(result);

        HandleCheckpoints(result);
        ViewState.Reset();
        return result;
    }

    // Renders `passageId` without creating a timeline entry (non-state-affecting navigation). The
    // current entry's cached render is overwritten in place so CurrentRender reflects it. At the
    // live edge, this also records/overwrites _activeState with the pre-render state captured below
    // — the same "capture before, render after" ordering PushAndRender uses, and for the same
    // reason: RenderChainFrom can throw, and _activeState must not change unless the render
    // actually succeeded (see RecoverFromFailedNavigation's own remarks on why this matters). The
    // post-checkpoint recheck guards a narrower case: if the passage being rendered contains its
    // own `checkpoint` node, HandleCheckpoints below already adds a real timeline entry for this
    // exact render — there's nothing left to track as "active" beyond an actual bookmark, so
    // _activeState is left as it was (null, or whatever it already held) rather than duplicating
    // that bookmark's own state. Away from the live edge (reachable only if a caller bypasses the
    // UI's IsRewound-gated disabling), there's no active state to track — a rewound RenderInPlace
    // doesn't correspond to "where play actually is."
    private PassageRenderResult RenderInPlace(string passageId)
    {
        var wasAtLiveEdge = HistoryIndex == _timeline.Count - 1;
        var preRenderVariables = wasAtLiveEdge ? _store.SessionSnapshot() : null;
        var preRenderSeedOccurrences = wasAtLiveEdge ? _prng.SnapshotOccurrences() : null;
        var timelineCountBeforeCheckpoints = _timeline.Count;

        var result = RenderChainFrom(passageId);
        _cachedRenders[HistoryIndex] = result;
        RecomputeVisitedFromTimeline();
        HandleCheckpoints(result);
        ViewState.Reset();

        if (wasAtLiveEdge && _timeline.Count == timelineCountBeforeCheckpoints)
        {
            _activeState = new ActiveState { PassageId = passageId, Variables = preRenderVariables!, SeedOccurrences = preRenderSeedOccurrences! };
            _viewingAnchor = false;
        }

        return result;
    }

    // Restores and re-renders the pending live-edge active state (see ActiveState's remarks) — its
    // counterpart to RestoreAndRerenderCurrent for the timeline's own entries. Deliberately does
    // NOT call HandleCheckpoints, mirroring RestoreAndRerenderCurrent: this is re-displaying state
    // that already happened once (when RenderInPlace originally captured it), not a fresh
    // navigation, so any checkpoint the passage contains was already registered then and shouldn't
    // be registered again. Doesn't clear _activeState — toggling back and forth between the anchor
    // and the active state should keep working, same as scrubbing ordinary timeline entries does.
    private PassageRenderResult RestoreActiveState()
    {
        var state = _activeState!;
        _store.RestoreSession(state.Variables);
        _prng.RestoreOccurrences(state.SeedOccurrences);
        RecomputeVisitedFromTimeline();
        ViewState.Reset();

        var result = RenderChainFrom(state.PassageId);
        _cachedRenders[HistoryIndex] = result;
        return result;
    }

    // Renders a passage, following any goto chain to its final landing passage.
    private PassageRenderResult RenderChainFrom(string passageId)
    {
        _visitedPassageIds.Add(passageId);
        var result = _passageRenderer.Render(GetPassageOrThrow(passageId), _store, _module, _visitedPassageIds);
        while (result.PendingGoto is not null)
        {
            var nextId = result.PendingGoto;
            _visitedPassageIds.Add(nextId);
            result = _passageRenderer.Render(GetPassageOrThrow(nextId), _store, _module, _visitedPassageIds);
        }
        return result;
    }

    // Every passage lookup in this class funnels through here specifically so a missing
    // passage_id (a typo in hand-authored content, or a target referencing a passage that hasn't
    // been written yet) produces a diagnosable PassageNotFoundException naming the id that was
    // requested, instead of a bare KeyNotFoundException from the dictionary indexer.
    private MwsPassageDoc GetPassageOrThrow(string passageId)
    {
        if (_module.Passages.TryGetValue(passageId, out var doc))
        {
            return doc;
        }

        _logger.LogError("Passage '{PassageId}' does not exist in this module.", passageId);
        throw new PassageNotFoundException(passageId);
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
                DisplayLabel = cp.Display ?? ResolveDisplayLabel(result),
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
    //
    // Reads result.Title/.Subtitle — already expanded by PassageRenderer against the live store,
    // as of the end of this same render (see PassageRenderResult.Title's own remarks) — rather
    // than re-deriving from the module's raw MwsPassageDoc.Title/.Subtitle. Those are
    // restext-resolved at load time but still contain unexpanded "{expr}" placeholders (a simple
    // "{randomname}" splice, or a dynamic ternary title collapsed from multiple branches by the
    // extractor — see docs/mws-format-latest.md and CradleExtractor.TryHoistHeadingTitleSubtitle)
    // — the timeline used to show that literal "{...}" text to the player instead of its evaluated
    // value, since nothing here ever called ExpandTemplate. Also naturally follows a goto chain
    // correctly: result.Title reflects the passage actually landed on and shown, not whatever
    // passage was originally requested before any goto redirected it.
    private static string ResolveDisplayLabel(PassageRenderResult result) =>
        result.Title is null
            ? result.PassageId
            : result.Subtitle is not null ? $"{result.Title} - {result.Subtitle}" : result.Title;

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

    // "app::gameover" — a module-authored signal that this playthrough is complete, distinct from
    // "${module::entrypoint}" in that it never resolves to a real passage at all. Unlike an ordinary
    // target it's a plain literal (no "${}" wrapper — see ResolveTarget's own fallthrough case),
    // since it isn't an expression to evaluate, just a fixed sentinel string a module's popup/link/
    // goto can use directly. See IsGameOverRequested's own remarks for what happens next.
    private const string AppGameOverTarget = "app::gameover";

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
