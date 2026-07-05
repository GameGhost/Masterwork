namespace Masterwork.Engine;

/// <summary>
/// Decouples <see cref="Expressions.ExpressionEvaluator"/> from <see cref="VariableStore"/>/<see cref="Session.SessionPrng"/>
/// so the evaluator can be tested in isolation with a fake context.
/// </summary>
public interface IStoryEvalContext
{
    /// <summary>Resolves a variable reference by name.</summary>
    /// <exception cref="StoryEvalException">No variable named <paramref name="name"/> exists.</exception>
    StoryValue GetVariable(string name);

    /// <summary>Returns a deterministic random integer in <c>[min, max]</c> for the given seed key.</summary>
    long RandBetween(long min, long max, string seedKey);

    /// <summary>Returns a deterministic permutation of <paramref name="items"/> for the given seed key.</summary>
    IReadOnlyList<StoryValue> Shuffled(IReadOnlyList<StoryValue> items, string seedKey);
}
