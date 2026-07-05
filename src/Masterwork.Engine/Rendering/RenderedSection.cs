namespace Masterwork.Engine.Rendering;

/// <summary>A rendered visually-distinct content container.</summary>
public sealed record RenderedSection(string? Title, string? Style, bool Collapsed, IReadOnlyList<RenderedNode> Content) : RenderedNode;
