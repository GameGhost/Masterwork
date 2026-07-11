namespace Masterwork.ModuleFormat;

/// <summary>Human-visible text content. The <c>type: text</c> node.</summary>
public sealed record TextNode : Node
{
    /// <inheritdoc/>
    public override string Type => "text";

    /// <summary>
    /// Formatted display string — may contain <c>{varName}</c> interpolation, <c>{icon:slug}</c>
    /// inline assets, and <c>**bold**</c>/<c>_italic_</c> markup.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>Horizontal alignment, if specified. Falls back to <see langword="null"/> (locale default) on an unrecognized value.</summary>
    public Alignment? Align { get; init; }

    /// <summary>Let-var names consumed by this text, for editor grouping.</summary>
    public IReadOnlyList<string> Lets { get; init; } = [];

    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }
}
