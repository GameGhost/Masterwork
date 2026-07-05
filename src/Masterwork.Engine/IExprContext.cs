namespace Masterwork.Engine;

/// <summary>
/// Decouples <see cref="ExpressionEvaluator"/> from <see cref="VariableStore"/>/<see cref="SessionPrng"/>
/// so the evaluator can be tested in isolation with a fake context.
/// </summary>
public interface IExprContext
{
    /// <summary>Resolves a variable reference by name.</summary>
    /// <exception cref="ExprEvalException">No variable named <paramref name="name"/> exists.</exception>
    ExprValue GetVariable(string name);

    /// <summary>Returns a deterministic random integer in <c>[min, max]</c> for the given seed key.</summary>
    long RandBetween(long min, long max, string seedKey);

    /// <summary>Returns a deterministic permutation of <paramref name="items"/> for the given seed key.</summary>
    IReadOnlyList<ExprValue> Shuffled(IReadOnlyList<ExprValue> items, string seedKey);
}
