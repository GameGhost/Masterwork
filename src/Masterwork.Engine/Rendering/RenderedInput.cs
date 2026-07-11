using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>
/// A rendered player-fillable field, shown inline. Implicitly required — see
/// <see cref="GameSession.IsInputValid"/>/<see cref="GameSession.AreCurrentInputsValid"/>.
/// </summary>
public sealed record RenderedInput : RenderedAction
{
    /// <summary>Formatted label shown inline with the field.</summary>
    public required string Label { get; init; }

    /// <summary>Session variable that receives the value.</summary>
    public required string Var { get; init; }

    /// <summary>The kind of value collected — derived from <see cref="Var"/>'s declared variable type, not parsed from the node.</summary>
    public required InputValueType InputType { get; init; }

    /// <summary>Minimum accepted value. Only meaningful when <see cref="InputType"/> is <see cref="InputValueType.Number"/>.</summary>
    public long? Min { get; init; }

    /// <summary>Maximum accepted value. Only meaningful when <see cref="InputType"/> is <see cref="InputValueType.Number"/>.</summary>
    public long? Max { get; init; }
}
