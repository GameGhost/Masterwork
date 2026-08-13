namespace Masterwork.Engine.Rendering;

/// <summary>
/// The output of rendering a passage: its rendered node tree, a flat list of interactive actions,
/// any checkpoints encountered, and — if the passage ended in an unconditional <c>goto</c> — the
/// target it's chaining to. <see cref="Nodes"/>/<see cref="Actions"/>/<see cref="Checkpoints"/> are
/// always empty when <see cref="PendingGoto"/> is set.
/// </summary>
public sealed record PassageRenderResult(
    string PassageId,
    string Layout,
    string? Title,
    string? Subtitle,
    string? LocationName,
    string? LocationIcon,
    IReadOnlyList<RenderedNode> Nodes,
    IReadOnlyList<RenderedAction> Actions,
    IReadOnlyList<RenderedCheckpoint> Checkpoints,
    string? PendingGoto,
    RenderedLayoutChrome Chrome
)
{
    /// <summary>
    /// Background-track override while this passage is topmost, or <see langword="null"/> to inherit
    /// from the enclosing tier (an open popup or the module default — see
    /// <see cref="Masterwork.Engine.Audio.AudioResolver"/>). Present-but-empty means explicit silence.
    /// </summary>
    public string? Music { get; init; }

    /// <summary>One-shot SFX fired when this passage becomes active, or <see langword="null"/> to use the module's <c>transition</c> default.</summary>
    public string? OnDisplaySound { get; init; }

    /// <summary>Milliseconds to delay <see cref="OnDisplaySound"/>.</summary>
    public int? OnDisplaySoundDelayMs { get; init; }
}
