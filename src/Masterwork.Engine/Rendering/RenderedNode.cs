namespace Masterwork.Engine.Rendering;

/// <summary>Base type for every node produced by rendering a passage.</summary>
public abstract record RenderedNode
{
    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }
}
