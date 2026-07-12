namespace Masterwork.Engine.Rendering;

/// <summary>
/// The output of rendering a passage: its rendered node tree, a flat list of interactive actions,
/// any checkpoints encountered, and — if the passage ended in an unconditional <c>goto</c> — the
/// target it's chaining to. <see cref="Nodes"/>/<see cref="Actions"/>/<see cref="Checkpoints"/> are
/// always empty when <see cref="PendingGoto"/> is set.
/// </summary>
public sealed record PassageRenderResult(
    string PassageId,
    string Layout,
    string? Title,
    string? Subtitle,
    string? LocationName,
    string? LocationIcon,
    IReadOnlyList<RenderedNode> Nodes,
    IReadOnlyList<RenderedAction> Actions,
    IReadOnlyList<RenderedCheckpoint> Checkpoints,
    string? PendingGoto,
    bool IsEnding,
    RenderedLayoutChrome Chrome
);
