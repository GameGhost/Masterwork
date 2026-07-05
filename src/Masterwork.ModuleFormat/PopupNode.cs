namespace Masterwork.ModuleFormat;

/// <summary>
/// A modal overlay, either click-triggered (<see cref="Label"/> set) or auto-displayed
/// (<see cref="Label"/> absent). The <c>type: popup</c> node.
/// </summary>
public sealed record PopupNode : Node
{
    /// <inheritdoc/>
    public override string Type => "popup";

    /// <summary>Formatted trigger label; omitted for an auto-displayed popup.</summary>
    public string? Label { get; init; }

    /// <summary>One of <c>link</c> or <c>button</c> — an open, module-extensible vocabulary.</summary>
    public string? Style { get; init; }

    /// <summary>Named layout that takes over the popup's full UI (e.g. <c>voting</c>, <c>setup</c>), if any.</summary>
    public string? Layout { get; init; }

    /// <summary>
    /// Content nodes — left unevaluated by the renderer; only evaluated (against a sandboxed
    /// store) when the popup is actually opened by the player.
    /// </summary>
    public IReadOnlyList<Node> Content { get; init; } = [];

    /// <summary>Target passage_id, or <c>"${expr}"</c>, navigated to when the popup closes.</summary>
    public string? OnClose { get; init; }

    /// <summary>Dismiss button label, if any.</summary>
    public string? Button { get; init; }

    /// <summary>Whether closing this popup creates a new timeline snapshot.</summary>
    public bool StateAffecting { get; init; }
}
