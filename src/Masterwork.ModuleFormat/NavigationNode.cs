namespace Masterwork.ModuleFormat;

/// <summary>A player-clickable link. The <c>type: navigation</c> node.</summary>
public sealed record NavigationNode : Node
{
    /// <inheritdoc/>
    public override string Type => "navigation";

    /// <summary>Formatted link label.</summary>
    public required string Label { get; init; }

    /// <summary>One of <c>link</c> (default) or <c>button</c> — an open, module-extensible vocabulary.</summary>
    public string? Style { get; init; }

    /// <summary>Target passage_id, or <c>"${expr}"</c> for a dynamic target resolved at follow time.</summary>
    public required string Target { get; init; }

    /// <summary>Whether following this link creates a new timeline snapshot.</summary>
    public required bool StateAffecting { get; init; }

    /// <summary>Optional display label for the timeline scrubber.</summary>
    public string? TimelineLabel { get; init; }

    /// <summary>
    /// Nodes executed when the link is followed, before <see cref="Target"/> is resolved — lets a
    /// dynamic target depend on state assigned here.
    /// </summary>
    public IReadOnlyList<Node> OnClick { get; init; } = [];
}
