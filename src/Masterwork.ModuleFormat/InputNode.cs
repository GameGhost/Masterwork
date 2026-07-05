namespace Masterwork.ModuleFormat;

/// <summary>A player-clickable form that collects a value and can be cancelled. The <c>type: input</c> node.</summary>
public sealed record InputNode : Node
{
    /// <inheritdoc/>
    public override string Type => "input";

    /// <summary>Formatted label for the button/link that opens the form.</summary>
    public required string Label { get; init; }

    /// <summary>One of <c>link</c> or <c>button</c> — an open, module-extensible vocabulary.</summary>
    public string? Style { get; init; }

    /// <summary>Formatted instruction text shown inside the form.</summary>
    public required string Text { get; init; }

    /// <summary>The kind of value collected. Unrecognized values are a hard module load error — see <see cref="Masterwork.ModuleFormat.InputValueType"/>.</summary>
    public required InputValueType InputType { get; init; }

    /// <summary>Session variable that receives the submitted value.</summary>
    public required string Var { get; init; }

    /// <summary>Target passage_id, or <c>"${expr}"</c>, navigated to after submission.</summary>
    public required string OnSubmit { get; init; }
}
