using System.Collections.Generic;

namespace Masterwork.Engine;

// Decouples the expression evaluator from VariableStore/SessionPrng so the evaluator can be
// tested in isolation with a fake context.
public interface IExprContext
{
    ExprValue GetVariable(string name);
    long RandBetween(long min, long max, string seedKey);
    IReadOnlyList<ExprValue> Shuffled(IReadOnlyList<ExprValue> items, string seedKey);
}
