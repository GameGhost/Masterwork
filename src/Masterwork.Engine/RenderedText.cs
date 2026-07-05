using Masterwork.ModuleFormat;

namespace Masterwork.Engine;

/// <summary>Rendered text content, with template placeholders already expanded.</summary>
public sealed record RenderedText(string Value, Alignment? Align) : RenderedNode;
