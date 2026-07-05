namespace Masterwork.Engine;

/// <summary>Thrown when an <see cref="ExprValue"/> conversion or evaluation step fails — e.g. an unknown variable, or an invalid type coercion.</summary>
public sealed class ExprEvalException(string message) : Exception(message);
