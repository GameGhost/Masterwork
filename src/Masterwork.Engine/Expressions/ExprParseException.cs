namespace Masterwork.Engine.Expressions;

/// <summary>Thrown when <see cref="ExpressionParser.Parse"/> encounters syntactically invalid MWS expression text.</summary>
public sealed class ExprParseException(string message) : Exception(message);
