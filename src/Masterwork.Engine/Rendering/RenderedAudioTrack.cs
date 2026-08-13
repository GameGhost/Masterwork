namespace Masterwork.Engine.Rendering;

/// <summary>
/// A rendered <c>audio_track</c> playback element — content, not an interactive action. Playback
/// state (play/pause, seek position) lives entirely in the UI, the same non-interactive treatment
/// <see cref="RenderedImage"/> gets.
/// </summary>
public sealed record RenderedAudioTrack(string Asset) : RenderedNode
{
    /// <summary>Formatted display label shown alongside the playback controls, if any.</summary>
    public string? Title { get; init; }

    /// <summary>
    /// <see langword="true"/> starts playback as soon as the element renders (subject to
    /// <see cref="AutoplayDelayMs"/>); <see langword="false"/> waits for the player to press play.
    /// </summary>
    public required bool Autoplay { get; init; }

    /// <summary>Milliseconds to wait before autoplay begins. <see langword="null"/> when <c>autoplay</c> was a bare bool (or absent).</summary>
    public int? AutoplayDelayMs { get; init; }

    /// <summary>How background music behaves while this track is actually playing: <c>"pause"</c>, <c>"duck"</c>, or <c>"none"</c>.</summary>
    public required string BgmBehavior { get; init; }
}
