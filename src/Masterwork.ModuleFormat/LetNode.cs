namespace Masterwork.ModuleFormat;

/// <summary>
/// A passage-scoped variable assignment — evaluated fresh on every render and never persisted to
/// the session/timeline. The <c>type: let</c> node.
/// </summary>
public sealed record LetNode : Node
{
    /// <inheritdoc/>
    public override string Type => "let";

    /// <summary>The let-variable name, visible only within the current passage render.</summary>
    public required string Var { get; init; }

    /// <summary>MWS expression to evaluate and assign to <see cref="Var"/>.</summary>
    public required string Expr { get; init; }
}
