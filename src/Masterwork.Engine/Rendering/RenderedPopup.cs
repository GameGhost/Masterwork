using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>
/// A rendered modal overlay. Its content is evaluated eagerly, at the same time as the rest of the
/// passage — against a private <see cref="Sandbox"/> clone of the store, so the popup's own
/// <c>assign</c> nodes never touch live state before the player accepts (see the popup transaction
/// model on <see cref="GameSession.ClosePopupAsync"/>). This means showing/hiding a popup is a pure
/// UI concern: no engine call is needed to open one, only <see cref="GameSession.ClosePopupAsync"/>
/// on Accept, to commit the sandbox and navigate.
/// </summary>
public sealed record RenderedPopup : RenderedAction
{
    /// <summary>Formatted trigger label; <see langword="null"/> for an auto-displayed popup.</summary>
    public string? Label { get; init; }

    /// <summary>Named layout that takes over the popup's full UI, if any.</summary>
    public string? Layout { get; init; }

    /// <summary><see langword="true"/> when this popup has no trigger label and displays automatically.</summary>
    public bool AutoDisplay { get; init; }

    /// <summary>The popup's rendered content, ready to display without any further engine call.</summary>
    public required IReadOnlyList<RenderedNode> Content { get; init; }

    /// <summary>
    /// Interactive actions (e.g. <c>input</c> nodes) found within <see cref="Content"/> — used to
    /// gate the Okay button via <see cref="GameSession.AreInputsValid"/> and to commit their values
    /// on Accept, the same way <see cref="Masterwork.Engine.PassageRenderResult"/>'s own
    /// <c>Actions</c> work for the enclosing passage.
    /// </summary>
    public required IReadOnlyList<RenderedAction> Actions { get; init; }

    /// <summary>
    /// The sandboxed store <see cref="Content"/> was rendered against. Engine-internal — commits to
    /// the live store only happen via <see cref="GameSession.ClosePopupAsync"/> on Accept.
    /// </summary>
    public required VariableStore Sandbox { get; init; }

    /// <summary>Formatted Okay button label; only rendered if present.</summary>
    public string? Okay { get; init; }

    /// <summary>Formatted Cancel button label; only rendered if present.</summary>
    public string? Cancel { get; init; }

    /// <summary>
    /// Unevaluated onclose nodes, run against <see cref="Sandbox"/> on Okay before resolving
    /// <see cref="Target"/> — same shape and timing as <see cref="RenderedLink.OnClickRaw"/>. A
    /// <c>goto</c> among these preempts <see cref="Target"/>.
    /// </summary>
    public required IReadOnlyList<Node> OnCloseRaw { get; init; }

    /// <summary>Target passage_id, or <c>"${expr}"</c>, navigated to when Okay is clicked (unless preempted by a <c>goto</c> in <see cref="OnCloseRaw"/>).</summary>
    public string? Target { get; init; }

    /// <summary>Whether closing this popup via Okay creates a new timeline snapshot.</summary>
    public required bool StateAffecting { get; init; }

    /// <summary>
    /// Custom label for the timeline scrubber entry created on Okay, set via <c>popup.snapshot</c>'s
    /// string form; overrides the destination passage's own title (the default — see
    /// <see cref="GameSession.ResolvePassageTitle"/>). A <c>goto</c> within <see cref="OnCloseRaw"/>
    /// that preempts <see cref="Target"/> takes priority over this, if that <c>goto</c> sets its own
    /// <c>snapshot_label</c>.
    /// </summary>
    public string? SnapshotLabel { get; init; }
}
