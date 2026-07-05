namespace Masterwork.Engine.Expressions;

/// <summary>One entry in an <see cref="Expr.ArrayLiteral"/>: either a plain item or a <c>..expr</c> spread.</summary>
public abstract record ArrayElement
{
    /// <summary>A single array element expression.</summary>
    public sealed record Item(Expr Value) : ArrayElement;

    /// <summary>A spread of another array's elements into this literal, e.g. <c>..elim</c>.</summary>
    public sealed record Spread(Expr Value) : ArrayElement;
}
