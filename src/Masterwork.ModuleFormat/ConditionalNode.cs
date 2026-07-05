namespace Masterwork.ModuleFormat;

/// <summary>
/// Branching logic. Either the flat form (a single <see cref="Conditions"/> entry with no
/// <see cref="Else"/>) or the multi-branch form (several <see cref="Conditions"/> entries plus an
/// optional <see cref="Else"/>). The <c>type: conditional</c> node.
/// </summary>
public sealed record ConditionalNode : Node
{
    /// <inheritdoc/>
    public override string Type => "conditional";

    /// <summary>Branches evaluated in order; the first one whose condition is true is rendered.</summary>
    public required IReadOnlyList<ConditionalBranch> Conditions { get; init; }

    /// <summary>Fallback nodes rendered when no branch condition is true, if present.</summary>
    public IReadOnlyList<Node>? Else { get; init; }
}
