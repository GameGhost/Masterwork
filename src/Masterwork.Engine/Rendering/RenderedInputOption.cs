namespace Masterwork.Engine.Rendering;

/// <summary>
/// Resolved form of a <see cref="Masterwork.ModuleFormat.InputOption"/> — both fields have already
/// gone through template expansion, same as every other display field.
/// </summary>
public sealed record RenderedInputOption
{
    /// <summary>The literal value committed to the bound variable when this option is selected.</summary>
    public required string Value { get; init; }

    /// <summary>Formatted label shown next to this option's radio control.</summary>
    public required string Label { get; init; }
}
