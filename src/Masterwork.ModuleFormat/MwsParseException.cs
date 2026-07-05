namespace Masterwork.ModuleFormat;

// Hard schema violation during passage parsing — missing required fields, malformed booleans, or
// enum fields with no valid fallback (e.g. input.input). Inherits FormatException so existing
// broad catches still work; use this specifically when the caller should be able to tell a module
// format problem apart from an unrelated parsing failure.
public sealed class MwsParseException(string message) : FormatException(message);
