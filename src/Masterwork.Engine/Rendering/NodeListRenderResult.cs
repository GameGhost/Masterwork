namespace Masterwork.Engine.Rendering;

/// <summary>
/// The result of rendering a standalone node list (popup content, a <c>link</c>'s <c>onclick</c>, a
/// popup's <c>onclose</c>, <c>include_passage</c> inlining): the rendered nodes, any interactive
/// actions found within them (own <see cref="RenderedAction.Id"/>s, prefixed via the
/// <c>actionIdPrefix</c> parameter of <see cref="IPassageRenderer.RenderNodeList"/> to stay unique
/// alongside the enclosing passage's own actions), and whether a <c>goto</c> fired during the
/// render — used to let a <c>goto</c> in a <c>link</c>/popup's on-click logic preempt its own
/// declared target. When it does, <see cref="PendingGotoLabel"/> carries that <c>goto</c>'s own
/// <c>timeline_label</c> (if any) — the caller gives it priority over the enclosing <c>link</c>/
/// <c>popup</c>'s own label, since the <c>goto</c> is what actually picked the destination.
/// </summary>
public sealed record NodeListRenderResult(
    IReadOnlyList<RenderedNode> Nodes,
    IReadOnlyList<RenderedAction> Actions,
    string? PendingGoto,
    string? PendingGotoLabel
);
