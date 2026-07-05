namespace Masterwork.Engine;

/// <summary>A rendered visually-distinct content container.</summary>
public sealed record RenderedSection(string? Title, string? Style, bool Collapsed, IReadOnlyList<RenderedNode> Content) : RenderedNode;
