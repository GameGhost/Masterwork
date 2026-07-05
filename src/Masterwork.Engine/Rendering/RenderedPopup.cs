using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>A rendered modal overlay trigger.</summary>
public sealed record RenderedPopup : RenderedAction
{
    /// <summary>Formatted trigger label; <see langword="null"/> for an auto-displayed popup.</summary>
    public string? Label { get; init; }

    /// <summary>One of <c>link</c> or <c>button</c>.</summary>
    public string? Style { get; init; }

    /// <summary>Named layout that takes over the popup's full UI, if any.</summary>
    public string? Layout { get; init; }

    /// <summary><see langword="true"/> when this popup has no trigger label and displays automatically.</summary>
    public bool AutoDisplay { get; init; }

    /// <summary>
    /// Unevaluated content nodes — only rendered when <see cref="GameSession.OpenPopupAsync"/> is
    /// called (see the popup transaction model: content evaluation is deferred until the popup opens).
    /// </summary>
    public required IReadOnlyList<Node> RawContent { get; init; }

    /// <summary>Target passage_id, or <c>"${expr}"</c>, navigated to when the popup closes.</summary>
    public string? OnClose { get; init; }

    /// <summary>Dismiss button label, if any.</summary>
    public string? Button { get; init; }

    /// <summary>Whether closing this popup creates a new timeline snapshot.</summary>
    public required bool StateAffecting { get; init; }
}
