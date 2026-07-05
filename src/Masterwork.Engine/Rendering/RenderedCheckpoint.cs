namespace Masterwork.Engine.Rendering;

/// <summary>A checkpoint milestone encountered while rendering a passage — becomes a <see cref="Session.SnapshotKind.Checkpoint"/> timeline entry.</summary>
public sealed record RenderedCheckpoint(string Id, string? Display, string? Diagnostic);
