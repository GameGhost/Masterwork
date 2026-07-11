namespace Masterwork.ModuleFormat;

/// <summary>A player-clickable link. The <c>type: link</c> node.</summary>
public sealed record LinkNode : Node
{
    /// <inheritdoc/>
    public override string Type => "link";

    /// <summary>Formatted link label.</summary>
    public required string Label { get; init; }

    /// <summary>Open, module-extensible visual style vocabulary (e.g. <c>link</c>, <c>button</c>) — styled entirely by module CSS.</summary>
    public string? Style { get; init; }

    /// <summary>Target passage_id, or <c>"${expr}"</c> for a dynamic target resolved at follow time.</summary>
    public required string Target { get; init; }

    /// <summary>Whether following this link creates a new timeline snapshot — parsed from <c>snapshot</c> (see <see cref="SnapshotLabel"/>).</summary>
    public required bool StateAffecting { get; init; }

    /// <summary>
    /// Display label for the timeline scrubber entry. Set by writing a string (instead of a bare
    /// bool) to <c>snapshot</c> — that string is both the label and an implicit <c>true</c> for
    /// <see cref="StateAffecting"/>.
    /// </summary>
    public string? SnapshotLabel { get; init; }

    /// <summary>
    /// Nodes executed when the link is followed, before <see cref="Target"/> is resolved — lets a
    /// dynamic target depend on state assigned here. A <c>goto</c> among these preempts <see cref="Target"/>.
    /// </summary>
    public IReadOnlyList<Node> OnClick { get; init; } = [];
}
