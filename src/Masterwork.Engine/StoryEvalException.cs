namespace Masterwork.Engine;

/// <summary>Thrown when an <see cref="StoryValue"/> conversion or evaluation step fails — e.g. an unknown variable, or an invalid type coercion.</summary>
public sealed class StoryEvalException(string message) : Exception(message);
