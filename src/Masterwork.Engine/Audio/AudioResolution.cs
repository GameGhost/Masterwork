namespace Masterwork.Engine.Audio;

/// <summary>
/// The effective background-music state at a point in time, as resolved by
/// <see cref="AudioResolver.ResolveMusic"/> — a closed union of the three distinct JS-side playback
/// shapes (nothing to play, one looping track, or an auto-advancing playlist), since those need
/// different playback behavior and a single "the music URL" string can't tell them apart.
/// </summary>
public abstract record AudioResolution
{
    /// <summary>No background music should be playing.</summary>
    public sealed record Silence : AudioResolution;

    /// <summary>A single track, looped.</summary>
    public sealed record SingleTrack(string Url) : AudioResolution;

    /// <summary>
    /// The module's own default playlist — plays through <see cref="Tracks"/>, auto-advancing when
    /// each track ends, per <see cref="Order"/> (<c>"sequence"</c> or <c>"shuffle"</c>). Only the
    /// module tier can produce this; passage/popup-level overrides are always a single track.
    /// </summary>
    public sealed record ModulePlaylist(IReadOnlyList<string> Tracks, string Order) : AudioResolution;
}
