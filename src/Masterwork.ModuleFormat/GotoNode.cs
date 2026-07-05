namespace Masterwork.ModuleFormat;

/// <summary>Unconditional navigation with no player interaction and no timeline snapshot. The <c>type: goto</c> node.</summary>
public sealed record GotoNode : Node
{
    /// <inheritdoc/>
    public override string Type => "goto";

    /// <summary>Target passage_id, or <c>"${expr}"</c> for a dynamic target, resolved immediately at render time.</summary>
    public required string Target { get; init; }
}
