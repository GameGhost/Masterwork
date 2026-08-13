namespace Masterwork.ModuleFormat;

/// <summary>
/// An in-passage audio playback element with a complete, module-styleable player UI (title,
/// play/pause, scrubber, current/total time) — the first narration-capable content node. The
/// <c>type: audio_track</c> node. Deliberately named apart from the <c>audio:</c> override block
/// (see <see cref="PassageAudio"/>/<see cref="PopupAudio"/>/<see cref="LinkAudio"/>) so the two
/// concepts are never visually confusable in a passage file.
/// </summary>
public sealed record AudioTrackNode : Node
{
    /// <inheritdoc/>
    public override string Type => "audio_track";

    /// <summary>Asset URI for the track (typically <c>audio://vo/...</c>), or <c>${expr}</c>.</summary>
    public required string Asset { get; init; }

    /// <summary>Formatted display label shown alongside the playback controls (string formatting — see the format spec).</summary>
    public string? Title { get; init; }

    /// <summary>Open, module-extensible visual style vocabulary, styled entirely by module CSS.</summary>
    public string? Style { get; init; }

    /// <summary><see langword="true"/> (default) starts playback as soon as the element renders; <see langword="false"/> waits for the player to press play. See <see cref="AutoplayDelayMs"/> for a delayed-autoplay start.</summary>
    public bool Autoplay { get; init; } = true;

    /// <summary>Milliseconds to wait before autoplay begins, if <see cref="Autoplay"/> is <see langword="true"/> via an integer <c>autoplay:</c> value. <see langword="null"/> when <c>autoplay</c> was a bare bool (or absent).</summary>
    public int? AutoplayDelayMs { get; init; }

    /// <summary>
    /// How background music behaves while this track is actually playing: <c>"pause"</c> (default),
    /// <c>"duck"</c>, or <c>"none"</c>. Has no effect while the track isn't playing (e.g. before the
    /// player presses play, if <see cref="Autoplay"/> is <see langword="false"/>).
    /// </summary>
    public string BgmBehavior { get; init; } = "pause";
}
