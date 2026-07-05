namespace Masterwork.Engine;

/// <summary>
/// The argument inside an <see cref="Expr.IndexAccess"/>'s brackets: either a single index
/// (<c>arr[N]</c> / <c>arr[^N]</c>) or a range (<c>arr[a..b]</c> / <c>arr[a..]</c> /
/// <c>arr[..b]</c>), using C# range semantics — the end index is exclusive.
/// </summary>
public abstract record IndexArg
{
    /// <summary>A single index, e.g. <c>arr[0]</c> or <c>arr[^1]</c> when <paramref name="FromEnd"/> is set.</summary>
    public sealed record Single(Expr Value, bool FromEnd) : IndexArg;

    /// <summary>
    /// A range, e.g. <c>arr[1..3]</c>. <see cref="Start"/>/<see cref="End"/> are <see langword="null"/>
    /// for an open-ended bound (<c>arr[1..]</c> / <c>arr[..3]</c>).
    /// </summary>
    public sealed record Range(Expr? Start, bool StartFromEnd, Expr? End, bool EndFromEnd) : IndexArg;
}
