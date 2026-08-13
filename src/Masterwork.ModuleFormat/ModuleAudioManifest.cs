namespace Masterwork.ModuleFormat;

/// <summary>
/// Module-level default background music, keyed by <see cref="ModuleAudioManifest.Music"/> on the
/// manifest's <c>audio:</c> section. Unlike node-level <c>audio:</c> fields, these are plain strings
/// — a manifest is parsed once, before any session exists, so there's no live variable scope to
/// evaluate a <c>${expr}</c> against.
/// </summary>
public sealed record ModuleMusicManifest
{
    /// <summary>
    /// <c>audio://</c> URIs for the module's default background track(s). Present-but-empty
    /// (<c>[]</c>) means explicit "no music by default anywhere in this module." Absent entirely has
    /// the same effective result — there's no further tier below the module default to fall back to.
    /// </summary>
    public IReadOnlyList<string> DefaultTracks { get; init; } = [];

    /// <summary>
    /// <c>"sequence"</c> (default) plays <see cref="DefaultTracks"/> through as one continuous
    /// playlist, auto-advancing when each track ends; <c>"shuffle"</c> does the same over a shuffled
    /// order, reshuffled each time this tier is freshly re-selected as the resolution winner.
    /// </summary>
    public string Order { get; init; } = "sequence";
}

/// <summary>
/// Module-level default sound effects, one candidate list per category — one entry is picked at
/// random per firing. See <c>docs/mws-format-latest.md</c> §9 for the full node-override-to-bucket
/// mapping (<c>on_display</c>→<see cref="Transition"/>, <c>open</c>→<see cref="PopupOpen"/>,
/// <c>okay</c>/<c>cancel</c> both→<see cref="PopupClose"/>).
/// </summary>
public sealed record ModuleSfxManifest
{
    /// <summary>Candidates for the passage-navigation transition sound.</summary>
    public IReadOnlyList<string> Transition { get; init; } = [];

    /// <summary>Default for popup-open, when a popup doesn't set its own <c>audio.open</c>.</summary>
    public IReadOnlyList<string> PopupOpen { get; init; } = [];

    /// <summary>Default for popup-close — used for both Okay and Cancel unless a popup sets its own <c>audio.okay</c>/<c>audio.cancel</c>.</summary>
    public IReadOnlyList<string> PopupClose { get; init; } = [];

    /// <summary>Default link-click sound, when a link doesn't set its own <c>audio.click</c>.</summary>
    public IReadOnlyList<string> Click { get; init; } = [];
}

/// <summary>The <c>audio:</c> top-level section of a module's <c>manifest.yaml</c>.</summary>
public sealed record ModuleAudioManifest
{
    /// <summary>Module-level default background music, if declared.</summary>
    public ModuleMusicManifest? Music { get; init; }

    /// <summary>Module-level default sound effects, if declared.</summary>
    public ModuleSfxManifest? Sfx { get; init; }
}
