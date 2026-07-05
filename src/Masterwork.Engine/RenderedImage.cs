using Masterwork.ModuleFormat;

namespace Masterwork.Engine;

/// <summary>A rendered standalone image.</summary>
public sealed record RenderedImage(string Asset, string? Size, Alignment? Align) : RenderedNode;
