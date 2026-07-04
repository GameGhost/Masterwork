namespace Masterwork.Engine;

// Parsed, immutable AST for the MWS expression language (see docs/mws-format-latest.md §4).
// Parsed once at module load time and cached per expression string; evaluation is a pure AST walk.
public abstract record Expr
{
    public sealed record IntLiteral(long Value) : Expr;
    public sealed record StringLiteral(string Value) : Expr;
    public sealed record BoolLiteral(bool Value) : Expr;
    public sealed record VarRef(string Name) : Expr;
    public sealed record PropertyAccess(Expr Target, string Property) : Expr;
    public sealed record IndexAccess(Expr Target, IndexArg Arg) : Expr;
    public sealed record Unary(string Op, Expr Operand) : Expr;
    public sealed record Binary(string Op, Expr Left, Expr Right) : Expr;
    public sealed record MethodCall(Expr Target, string Method, IReadOnlyList<Expr> Args) : Expr;
    public sealed record FunctionCall(string Name, IReadOnlyList<Expr> Args) : Expr;
    public sealed record ArrayLiteral(IReadOnlyList<ArrayElement> Elements) : Expr;
    public sealed record RecordLiteral(IReadOnlyDictionary<string, Expr> Properties) : Expr;
}

// One entry in an array literal: either a plain item or a `..expr` spread.
public abstract record ArrayElement
{
    public sealed record Item(Expr Value) : ArrayElement;
    public sealed record Spread(Expr Value) : ArrayElement;
}

// `arr[N]` / `arr[^N]` (Single), or `arr[a..b]` / `arr[a..]` / `arr[..b]` (Range).
// C# range semantics: end index is exclusive.
public abstract record IndexArg
{
    public sealed record Single(Expr Value, bool FromEnd) : IndexArg;
    public sealed record Range(Expr? Start, bool StartFromEnd, Expr? End, bool EndFromEnd) : IndexArg;
}
