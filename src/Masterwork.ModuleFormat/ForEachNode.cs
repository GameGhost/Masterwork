namespace Masterwork.ModuleFormat;

/// <summary>Iteration over an array variable. The <c>type: foreach</c> node.</summary>
public sealed record ForEachNode : Node
{
    /// <inheritdoc/>
    public override string Type => "foreach";

    /// <summary>Loop variable name, exposed as a let-var within <see cref="Do"/>.</summary>
    public required string Var { get; init; }

    /// <summary>The array variable to iterate.</summary>
    public required string In { get; init; }

    /// <summary>Nodes rendered once per element.</summary>
    public required IReadOnlyList<Node> Do { get; init; }
}
