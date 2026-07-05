namespace Masterwork.ModuleFormat;

/// <summary>
/// An inline prompt that interrupts rendering until submitted and cannot be cancelled. The
/// <c>type: prompt</c> node. Spec'd but not yet emitted by the extractor.
/// </summary>
public sealed record PromptNode : Node
{
    /// <inheritdoc/>
    public override string Type => "prompt";

    /// <summary>Formatted instruction text.</summary>
    public required string Text { get; init; }

    /// <summary>The kind of value collected. Unrecognized values are a hard module load error.</summary>
    public required InputValueType InputType { get; init; }

    /// <summary>Session variable that receives the submitted value.</summary>
    public required string Var { get; init; }
}
