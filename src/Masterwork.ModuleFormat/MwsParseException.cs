namespace Masterwork.ModuleFormat;

/// <summary>
/// A hard schema violation encountered while parsing a passage or the variable manifest — a
/// missing required field, a malformed boolean, or an enum field with no valid fallback (e.g.
/// <c>input.input</c>). Inherits <see cref="FormatException"/> so existing broad catches still
/// work; throw/catch this specifically when the caller needs to tell a module format problem
/// apart from an unrelated parsing failure.
/// </summary>
/// <param name="message">Description including the source (passage ID or file) and field name.</param>
public sealed class MwsParseException(string message) : FormatException(message);
