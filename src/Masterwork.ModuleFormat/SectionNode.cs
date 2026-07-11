namespace Masterwork.ModuleFormat;

/// <summary>A visually-distinct content container. The <c>type: section</c> node.</summary>
public sealed record SectionNode : Node
{
    /// <inheritdoc/>
    public override string Type => "section";

    /// <summary>Optional section title, formatted like <see cref="TextNode.Value"/>.</summary>
    public string? Title { get; init; }

    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }

    /// <summary>Whether the section starts collapsed in the UI.</summary>
    public bool Collapsed { get; init; }

    /// <summary>Child nodes rendered inside the section.</summary>
    public IReadOnlyList<Node> Content { get; init; } = [];
}
