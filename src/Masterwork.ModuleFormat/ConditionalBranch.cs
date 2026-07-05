namespace Masterwork.ModuleFormat;

/// <summary>
/// One <c>if</c>/<c>then</c> branch of a <see cref="ConditionalNode"/>'s multi-branch form
/// (<c>conditions: [{if, then}]</c>).
/// </summary>
public sealed record ConditionalBranch
{
    /// <summary>MWS boolean expression guarding this branch.</summary>
    public required string If { get; init; }

    /// <summary>Nodes rendered when <see cref="If"/> evaluates true.</summary>
    public required IReadOnlyList<Node> Then { get; init; }
}
