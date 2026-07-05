namespace Masterwork.Engine;

/// <summary>
/// Evaluates a parsed <see cref="Expr"/> AST (or a raw expression string) against an
/// <see cref="IExprContext"/>.
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>Parses (or retrieves from cache) and evaluates an expression string against <paramref name="ctx"/>.</summary>
    ExprValue Evaluate(string source, IExprContext ctx);

    /// <summary>Evaluates a parsed <see cref="Expr"/> AST against <paramref name="ctx"/>.</summary>
    ExprValue Evaluate(Expr expr, IExprContext ctx);

    /// <summary>
    /// Matches a value against a pattern string used by <c>switch.cases[i].match</c> and
    /// <c>arr.countif(pattern)</c>: a bare value (equality), or <c>=value</c>, <c>&gt;value</c>,
    /// <c>&gt;=value</c>, <c>&lt;value</c>, <c>&lt;=value</c>, <c>!=value</c>. Property patterns
    /// for arrays of custom types use <c>"property: pattern"</c>.
    /// </summary>
    bool MatchesPattern(ExprValue value, string pattern);
}
