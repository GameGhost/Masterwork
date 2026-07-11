using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>A rendered standalone image.</summary>
public sealed record RenderedImage(string Asset, string? Size, Alignment? Align) : RenderedNode
{
    /// <summary>Formatted title/alt text for the image, if any.</summary>
    public string? Title { get; init; }
}
