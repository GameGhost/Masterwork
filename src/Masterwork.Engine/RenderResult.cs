using Masterwork.ModuleFormat;

namespace Masterwork.Engine;

// A render is either a normal passage render, or an unconditional goto that stopped rendering
// before producing any content — Nodes/Actions/Checkpoints are always empty when PendingGoto is set.
public sealed record PassageRenderResult(
    string PassageId,
    string? LocationName,
    string? LocationIcon,
    IReadOnlyList<RenderedNode> Nodes,
    IReadOnlyList<RenderedAction> Actions,
    IReadOnlyList<RenderedCheckpoint> Checkpoints,
    string? PendingGoto
);

public sealed record RenderedCheckpoint(string Id, string? Display, string? Diagnostic);

public abstract record RenderedNode;

// Interactive nodes are both a RenderedNode (rendered inline at their tree position) and
// collected into PassageRenderResult.Actions (a flat list for the App to bind without walking
// the tree). Each gets a stable Id, unique within a single passage render.
public abstract record RenderedAction : RenderedNode
{
    public required string Id { get; init; }
}

public sealed record RenderedText(string Value, Alignment? Align) : RenderedNode;
public sealed record RenderedBreak : RenderedNode;
public sealed record RenderedParagraphBreak : RenderedNode;
public sealed record RenderedImage(string Asset, string? Size, Alignment? Align) : RenderedNode;

public sealed record RenderedSection(string? Title, string? Style, bool Collapsed, IReadOnlyList<RenderedNode> Content) : RenderedNode;

public sealed record RenderedNavigation : RenderedAction
{
    public required string Label { get; init; }
    public string? Style { get; init; }
    // Raw target string, possibly "${expr}" — resolved by GameSession.FollowLinkAsync, not here.
    public required string Target { get; init; }
    public required bool StateAffecting { get; init; }
    public string? TimelineLabel { get; init; }
    // Unevaluated — GameSession runs these on FollowLinkAsync, before resolving a dynamic target.
    public required IReadOnlyList<Node> OnClickRaw { get; init; }
}

public sealed record RenderedPopup : RenderedAction
{
    public string? Label { get; init; }
    public string? Style { get; init; }
    public string? Layout { get; init; }
    public bool AutoDisplay { get; init; }
    // Unevaluated — content is only rendered when GameSession.OpenPopupAsync is called (see the
    // popup transaction model: content evaluation is deferred until the player opens the popup).
    public required IReadOnlyList<Node> RawContent { get; init; }
    public string? OnClose { get; init; }
    public string? Button { get; init; }
    public required bool StateAffecting { get; init; }
}

public sealed record RenderedInput : RenderedAction
{
    public required string Label { get; init; }
    public string? Style { get; init; }
    public required string Text { get; init; }
    public required InputValueType InputType { get; init; }
    public required string Var { get; init; }
    public required string OnSubmit { get; init; }
}

// Result of opening a popup: its content, rendered against the pending (not-yet-committed) state
// produced by evaluating the popup's content nodes.
public sealed record PopupRenderResult(IReadOnlyList<RenderedNode> Content);
