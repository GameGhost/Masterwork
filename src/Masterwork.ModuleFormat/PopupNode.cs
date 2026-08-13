namespace Masterwork.ModuleFormat;

/// <summary>
/// A modal overlay, either click-triggered (<see cref="Label"/> set) or auto-displayed
/// (<see cref="Label"/> absent). The <c>type: popup</c> node. Its <see cref="Okay"/>/<see cref="Cancel"/>
/// dismissal mirrors <see cref="LinkNode"/>'s own <c>onclick</c>/<c>target</c> shape: <see cref="OnClose"/>
/// runs first (its own <c>goto</c>, if any, preempts <see cref="Target"/>), then <see cref="Target"/>
/// resolves the destination.
/// </summary>
public sealed record PopupNode : Node
{
    /// <inheritdoc/>
    public override string Type => "popup";

    /// <summary>Formatted trigger label; omitted for an auto-displayed popup.</summary>
    public string? Label { get; init; }

    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }

    /// <summary>Named layout that takes over the popup's full UI (e.g. <c>voting</c>, <c>setup</c>), if any.</summary>
    public string? Layout { get; init; }

    /// <summary>
    /// Optional nodes rendered in a separate structural region, before <see cref="Content"/> —
    /// e.g. the extractor places a Cradle <c>setupStyle</c> block's image here. Evaluated eagerly
    /// alongside <see cref="Content"/>, against the same sandboxed store. Purely structural: the
    /// format doesn't prescribe what a header contains or how it's positioned — that's entirely up
    /// to module CSS. The App renders whatever's here without inspecting it.
    /// </summary>
    public IReadOnlyList<Node> Header { get; init; } = [];

    /// <summary>
    /// Content nodes — evaluated eagerly alongside the rest of the passage, against a sandboxed
    /// store, so showing the popup is a pure UI concern (see <c>Masterwork.Engine.Rendering.RenderedPopup</c>'s
    /// remarks). May contain <c>input</c> nodes.
    /// </summary>
    public IReadOnlyList<Node> Content { get; init; } = [];

    /// <summary>
    /// Formatted Okay button label; only rendered if present. Committing input drafts + running
    /// <see cref="OnClose"/> + resolving <see cref="Target"/> only happens when Okay is clicked.
    /// Both <see cref="OnClose"/> and <see cref="Target"/> are optional — if neither is set, Okay
    /// still commits any input drafts but otherwise just closes the popup with no navigation and
    /// no engine round-trip beyond that commit (e.g. a purely informational popup that only needs
    /// an acknowledgement button).
    /// </summary>
    public string? Okay { get; init; }

    /// <summary>Formatted Cancel button label; only rendered if present. Discards pending state — no <see cref="OnClose"/>, no <see cref="Target"/>, no commit.</summary>
    public string? Cancel { get; init; }

    /// <summary>
    /// Nodes executed when Okay is clicked, before <see cref="Target"/> is resolved — same shape
    /// and timing as <see cref="LinkNode.OnClick"/>. A <c>goto</c> among these preempts <see cref="Target"/>.
    /// </summary>
    public IReadOnlyList<Node> OnClose { get; init; } = [];

    /// <summary>Target passage_id, or <c>"${expr}"</c>, navigated to when Okay is clicked (unless preempted by a <c>goto</c> in <see cref="OnClose"/>).</summary>
    public string? Target { get; init; }

    /// <summary>Whether closing this popup via Okay creates a new timeline snapshot — parsed from <c>snapshot</c> (see <see cref="SnapshotLabel"/>).</summary>
    public bool StateAffecting { get; init; }

    /// <summary>
    /// Display label for the timeline scrubber entry created when Okay is clicked; overrides the
    /// destination passage's own <c>title</c> (the default). Set by writing a string (instead of a
    /// bare bool) to <c>snapshot</c> — that string is both the label and an implicit <c>true</c> for
    /// <see cref="StateAffecting"/>. A <c>goto</c> inside <see cref="OnClose"/> that preempts
    /// <see cref="Target"/> takes priority over this — and over <see cref="StateAffecting"/> itself
    /// — if that <c>goto</c> sets its own <c>snapshot</c>; see <see cref="GotoNode.StateAffecting"/>.
    /// </summary>
    public string? SnapshotLabel { get; init; }

    /// <summary>Background-music/open/okay/cancel sound overrides for this popup, if declared.</summary>
    public PopupAudio? Audio { get; init; }
}
