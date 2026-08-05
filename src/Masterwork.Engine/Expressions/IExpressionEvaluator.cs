namespace Masterwork.Engine.Expressions;

/// <summary>
/// Evaluates a parsed <see cref="Expr"/> AST (or a raw expression string) against an
/// <see cref="IStoryEvalContext"/>.
/// </summary>
public interface IExpressionEvaluator
{
    /// <summary>Parses (or retrieves from cache) and evaluates an expression string against <paramref name="ctx"/>.</summary>
    StoryValue Evaluate(string source, IStoryEvalContext ctx);

    /// <summary>Evaluates a parsed <see cref="Expr"/> AST against <paramref name="ctx"/>.</summary>
    StoryValue Evaluate(Expr expr, IStoryEvalContext ctx);

    /// <summary>
    /// Matches a value against a pattern string used by <c>switch.cases[i].match</c> and
    /// <c>arr.countif(pattern)</c>: a bare value (equality), or <c>=value</c>, <c>&gt;value</c>,
    /// <c>&gt;=value</c>, <c>&lt;value</c>, <c>&lt;=value</c>, <c>!=value</c>. Property patterns
    /// for arrays of custom types use <c>"property: pattern"</c>.
    /// </summary>
    bool MatchesPattern(StoryValue value, string pattern);

    /// <summary>
    /// Replaces each <c>{expr}</c> placeholder in <paramref name="template"/> with the stringified
    /// result of evaluating <c>expr</c> against <paramref name="ctx"/>. A <c>{icon:slug}</c>
    /// placeholder is left untouched (resolved later, at text-render time, not here). Shared by
    /// display-text templates (<see cref="VariableStore.ExpandTemplate"/>) and quoted string
    /// literals inside expressions, which support the same <c>{expr}</c> syntax — see
    /// <see cref="Expr.StringLiteral"/>'s evaluation in <see cref="ExpressionEvaluator"/>.
    /// </summary>
    string ExpandTemplate(string template, IStoryEvalContext ctx);
}
