namespace Masterwork.ModuleFormat;

/// <summary>
/// One radio-style choice in an <c>input</c> node's <see cref="InputNode.Options"/> list. Both
/// fields are restext-eligible (resolved via <see cref="RestextResolver"/>) — <see cref="Value"/>
/// isn't expected to actually contain a <c>restext://</c> reference in practice, but is treated
/// uniformly with <see cref="Label"/> rather than special-cased out of resolution.
/// </summary>
public sealed record InputOption
{
    /// <summary>
    /// The literal value committed to <see cref="InputNode.Var"/> when this option is selected,
    /// converted per the variable's own declared type (see <c>docs/mws-format-latest.md</c>'s
    /// <c>input</c> section) — e.g. <c>"true"</c>/<c>"false"</c> for a boolean-typed variable,
    /// a parseable integer string for a numeric one, or any string for a string-typed one.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>Formatted label shown next to this option's radio control.</summary>
    public required string Label { get; init; }
}
