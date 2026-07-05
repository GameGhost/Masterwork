namespace Masterwork.Engine.Expressions;

/// <summary>
/// Recursive-descent parser for the MWS expression language. Whitelist grammar only — no
/// arbitrary code execution. See <c>docs/mws-format-latest.md</c> §4 for the full operator/function
/// reference.
/// </summary>
public interface IExpressionParser
{
    /// <summary>Parses an MWS expression string into an <see cref="Expr"/> AST.</summary>
    /// <exception cref="ExprParseException">The expression text is syntactically invalid.</exception>
    Expr Parse(string source);
}
