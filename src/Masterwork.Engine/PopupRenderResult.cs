namespace Masterwork.Engine;

/// <summary>
/// The result of opening a popup: its content, rendered against the pending (not-yet-committed)
/// state produced by evaluating the popup's content nodes. See <see cref="GameSession.OpenPopupAsync"/>.
/// </summary>
public sealed record PopupRenderResult(IReadOnlyList<RenderedNode> Content);
