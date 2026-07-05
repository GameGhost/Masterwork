namespace Masterwork.Engine;

/// <summary>
/// Base type for interactive rendered nodes. These are both a <see cref="RenderedNode"/>
/// (rendered inline at their tree position) and collected into
/// <see cref="PassageRenderResult.Actions"/> (a flat list for the App to bind without walking the
/// tree). Each gets an <see cref="Id"/> that's stable and unique within a single passage render.
/// </summary>
public abstract record RenderedAction : RenderedNode
{
    /// <summary>Stable identifier for this action within the current passage render (e.g. <c>"nav_0"</c>).</summary>
    public required string Id { get; init; }
}
