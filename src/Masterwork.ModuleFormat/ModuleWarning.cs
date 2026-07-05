namespace Masterwork.ModuleFormat;

/// <summary>
/// A single issue recorded while loading a module — e.g. a missing restext key, an unresolved
/// passage reference, or a field with an unexpected shape. See <see cref="ModuleWarnings"/> for
/// the collection this accumulates into.
/// </summary>
/// <param name="Kind">
/// Short machine-readable category (e.g. "missing_restext_key", "unmatched_field",
/// "wrong_field_type"), useful for grouping/filtering warnings programmatically.
/// </param>
/// <param name="Message">Human-readable description, including the source it came from.</param>
public sealed record ModuleWarning(string Kind, string Message);
