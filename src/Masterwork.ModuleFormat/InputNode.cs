namespace Masterwork.ModuleFormat;

/// <summary>
/// A player-fillable field, rendered inline wherever it appears (in a passage or inside a
/// <c>popup</c>'s content). Implicitly required — the field starts empty and any <c>link</c> in the
/// same passage (or a popup's <c>okay</c> action) stays disabled until every <c>input</c> currently
/// showing has a valid value, at which point following that link commits all of them to their
/// bound variables as part of its own snapshot. The <c>type: input</c> node.
/// </summary>
public sealed record InputNode : Node
{
    /// <inheritdoc/>
    public override string Type => "input";

    /// <summary>Formatted label shown inline with the field.</summary>
    public required string Label { get; init; }

    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }

    /// <summary>
    /// Session variable that receives the value. The field's value type (string vs. number) is
    /// derived from this variable's own declared type — there is no separate type declaration on
    /// the node itself.
    /// </summary>
    public required string Var { get; init; }

    /// <summary>Minimum accepted value. Only meaningful when <see cref="Var"/>'s declared type is numeric; otherwise ignored.</summary>
    public long? Min { get; init; }

    /// <summary>Maximum accepted value. Only meaningful when <see cref="Var"/>'s declared type is numeric; otherwise ignored.</summary>
    public long? Max { get; init; }
}
