namespace Masterwork.ModuleFormat;

/// <summary>A single line break. The <c>type: break</c> node.</summary>
public sealed record BreakNode : Node
{
    /// <inheritdoc/>
    public override string Type => "break";
}
