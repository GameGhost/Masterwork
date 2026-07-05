namespace Masterwork.ModuleFormat;

/// <summary>Inline passage embedding — the target's nodes are rendered as if they were part of the current passage, with no timeline snapshot. The <c>type: include_passage</c> node.</summary>
public sealed record IncludePassageNode : Node
{
    /// <inheritdoc/>
    public override string Type => "include_passage";

    /// <summary>Target passage_id, or <c>"${expr}"</c> for a dynamic target, resolved immediately at render time.</summary>
    public required string Target { get; init; }
}
