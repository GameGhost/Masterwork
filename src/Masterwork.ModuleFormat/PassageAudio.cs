namespace Masterwork.ModuleFormat;

/// <summary>
/// The <c>audio:</c> header on a passage — background-music override while this passage is
/// topmost, and a one-shot sound fired when it becomes active. See
/// <c>docs/mws-format-latest.md</c> §1 for the absent/empty-string/URI resolution semantics.
/// </summary>
public sealed record PassageAudio
{
    /// <summary>
    /// Background-track override, or <c>${expr}</c>. Absent (<see langword="null"/>) means inherit
    /// from the module default; present-but-empty (<c>""</c>) means explicit silence while this
    /// passage is topmost.
    /// </summary>
    public string? Music { get; init; }

    /// <summary>SFX fired once when this passage becomes the active passage, or <c>${expr}</c>. Same absent/empty semantics as <see cref="Music"/>.</summary>
    public string? OnDisplay { get; init; }

    /// <summary>Milliseconds to delay <see cref="OnDisplay"/> before it plays, to sync with a transition/animation. Default <c>0</c>.</summary>
    public int? OnDisplayDelayMs { get; init; }
}
