using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>A rendered player-clickable link.</summary>
public sealed record RenderedNavigation : RenderedAction
{
    /// <summary>Formatted link label.</summary>
    public required string Label { get; init; }

    /// <summary>One of <c>link</c> or <c>button</c>.</summary>
    public string? Style { get; init; }

    /// <summary>Raw target string, possibly <c>"${expr}"</c> — resolved by <see cref="GameSession.FollowLinkAsync"/>, not here.</summary>
    public required string Target { get; init; }

    /// <summary>Whether following this link creates a new timeline snapshot.</summary>
    public required bool StateAffecting { get; init; }

    /// <summary>Optional display label for the timeline scrubber.</summary>
    public string? TimelineLabel { get; init; }

    /// <summary>
    /// Unevaluated onclick nodes — <see cref="GameSession"/> runs these on
    /// <see cref="GameSession.FollowLinkAsync"/>, before resolving a dynamic target.
    /// </summary>
    public required IReadOnlyList<Node> OnClickRaw { get; init; }
}
