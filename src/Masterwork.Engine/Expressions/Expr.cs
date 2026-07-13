namespace Masterwork.Engine.Expressions;

/// <summary>
/// Parsed, immutable AST for the MWS expression language (see <c>docs/mws-format-latest.md</c>
/// §4). Parsed once at module load time and cached per expression string (see
/// <see cref="ExpressionEvaluator.GetOrParse"/>); evaluation is a pure AST walk.
/// </summary>
public abstract record Expr
{
    /// <summary>An integer literal, e.g. <c>42</c>.</summary>
    public sealed record IntLiteral(long Value) : Expr;

    /// <summary>A double-quoted string literal, e.g. <c>"yes"</c>.</summary>
    public sealed record StringLiteral(string Value) : Expr;

    /// <summary>The literal <c>true</c> or <c>false</c>.</summary>
    public sealed record BoolLiteral(bool Value) : Expr;

    /// <summary>A bare variable reference, e.g. <c>round</c>.</summary>
    public sealed record VarRef(string Name) : Expr;

    /// <summary>Dot-property access on a record value, e.g. <c>entry.points</c>.</summary>
    public sealed record PropertyAccess(Expr Target, string Property) : Expr;

    /// <summary>Index or range access, e.g. <c>arr[0]</c>, <c>arr[^1]</c>, <c>arr[1..3]</c>.</summary>
    public sealed record IndexAccess(Expr Target, IndexArg Arg) : Expr;

    /// <summary>A prefix unary operator: <c>!</c> (logical not) or <c>-</c> (negation).</summary>
    public sealed record Unary(string Op, Expr Operand) : Expr;

    /// <summary>A binary operator application (arithmetic, comparison, or logical).</summary>
    public sealed record Binary(string Op, Expr Left, Expr Right) : Expr;

    /// <summary>A ternary conditional, e.g. <c>round == 1 ? "Fever1" : "Fever2"</c>. Right-associative, so chained ternaries in <see cref="WhenFalse"/> read as an if/else-if chain.</summary>
    public sealed record Ternary(Expr Condition, Expr WhenTrue, Expr WhenFalse) : Expr;

    /// <summary>A method call on a string or array value, e.g. <c>arr.shuffled("key")</c>.</summary>
    public sealed record MethodCall(Expr Target, string Method, IReadOnlyList<Expr> Args) : Expr;

    /// <summary>A built-in function call, e.g. <c>rand_between(1, 6, "key")</c>.</summary>
    public sealed record FunctionCall(string Name, IReadOnlyList<Expr> Args) : Expr;

    /// <summary>An array literal, e.g. <c>[a, b, ..rest]</c>. See <see cref="ArrayElement"/> for spread support.</summary>
    public sealed record ArrayLiteral(IReadOnlyList<ArrayElement> Elements) : Expr;

    /// <summary>A record literal, e.g. <c>{ name: nameA, points: 1 }</c>.</summary>
    public sealed record RecordLiteral(IReadOnlyDictionary<string, Expr> Properties) : Expr;
}
