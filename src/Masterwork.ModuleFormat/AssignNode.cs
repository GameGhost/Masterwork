namespace Masterwork.ModuleFormat;

/// <summary>
/// A persistent session variable write — saved as part of the timeline snapshot that follows it.
/// The <c>type: assign</c> node.
/// </summary>
public sealed record AssignNode : Node
{
    /// <inheritdoc/>
    public override string Type => "assign";

    /// <summary>The session variable name to write to.</summary>
    public required string Var { get; init; }

    /// <summary>MWS expression to evaluate and assign to <see cref="Var"/>.</summary>
    public required string Expr { get; init; }
}
