using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>A rendered standalone image.</summary>
public sealed record RenderedImage(string Asset, string? Size, Alignment? Align) : RenderedNode;
