namespace Masterwork.ModuleFormat;

/// <summary>
/// A line break within a content block. The <c>type: break</c> node. A paragraph separator (a
/// larger visual gap) is <c>{type: 'break', style: 'paragraph'}</c> — module CSS decides what
/// <c>style-paragraph</c> actually looks like; the engine no longer distinguishes them structurally.
/// </summary>
public sealed record BreakNode : Node
{
    /// <inheritdoc/>
    public override string Type => "break";

    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }
}
