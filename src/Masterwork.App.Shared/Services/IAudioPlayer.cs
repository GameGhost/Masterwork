using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Plays background music (with crossfade, including an auto-advancing playlist for a module's
/// default tracks), one-shot sound effects, addressable/seekable <c>audio_track</c> playback, and
/// reduces volume to an "ambient" level while the timeline is rewound. One implementation
/// (<see cref="JsAudioPlayer"/>) works on every host — MAUI's <c>BlazorWebView</c> is a real
/// WebView, so plain HTML5 audio + the Web Audio API (via <see cref="Microsoft.JSInterop.IJSRuntime"/>)
/// behave the same there as in a browser, unlike storage (<see cref="ISaveStore"/>), which
/// genuinely needs different native APIs per host.
/// </summary>
public interface IAudioPlayer
{
    /// <summary>Starts (or crossfades to) a single looping background track.</summary>
    Task PlayBgmAsync(string? url);

    /// <summary>
    /// Starts (or crossfades to) the module's own default background music as an auto-advancing
    /// playlist — <paramref name="order"/> is <c>"sequence"</c> or <c>"shuffle"</c> (see
    /// <see cref="Masterwork.Engine.Audio.AudioResolution.ModulePlaylist"/>). A single-element
    /// <paramref name="urls"/> behaves exactly like <see cref="PlayBgmAsync"/> — nothing to advance
    /// to, so it just loops.
    /// </summary>
    Task PlayBgmPlaylistAsync(IReadOnlyList<string> urls, string order);

    /// <summary>Fades out and stops the current background track or playlist, if any.</summary>
    Task StopBgmAsync();

    /// <summary>Plays a one-shot sound effect.</summary>
    Task PlaySfxAsync(string? url);

    /// <summary>Reduces (or restores) background music volume to an "ambient" level — wired to <see cref="Masterwork.Engine.GameSession.IsRewound"/>.</summary>
    Task SetAmbientOnlyAsync(bool ambientOnly);

    /// <summary>Sets background music volume, 0.0-1.0 — the Options dialog's Background Volume slider.</summary>
    Task SetBgmVolumeAsync(double volume);

    /// <summary>Mutes/unmutes background music independent of its volume level — the Options dialog's Mute toggle.</summary>
    Task SetBgmMutedAsync(bool muted);

    /// <summary>Sets sound effect volume, 0.0-1.0 — the Options dialog's Sound Effects Volume slider. Also applies live to every currently-loaded <c>audio_track</c> (see <see cref="LoadTrackAsync"/>), not just future one-shot SFX.</summary>
    Task SetSfxVolumeAsync(double volume);

    /// <summary>Mutes/unmutes sound effects independent of their volume level — the Options dialog's Mute toggle. Also applies live to every currently-loaded <c>audio_track</c>.</summary>
    Task SetSfxMutedAsync(bool muted);

    /// <summary>
    /// Loads (or reloads) an addressable, independently-controllable track for an <c>audio_track</c>
    /// element, keyed by <paramref name="handle"/> — unique per rendered element (its
    /// <see cref="Masterwork.Engine.Rendering.RenderedAudioTrack"/> doesn't carry an id of its own,
    /// since it isn't a <c>RenderedAction</c>, so the caller mints one, e.g. from its position in the
    /// node tree). <paramref name="bgmBehavior"/> is <c>"pause"</c>, <c>"duck"</c>, or <c>"none"</c> —
    /// applied to the current background music only while this track is actually playing. Does not
    /// start playback; call <see cref="PlayTrackAsync"/> (or rely on the caller's own autoplay
    /// handling) separately.
    /// </summary>
    Task LoadTrackAsync(string handle, string url, string bgmBehavior);

    /// <summary>Starts or resumes playback of a track loaded via <see cref="LoadTrackAsync"/>. No-op if unknown.</summary>
    Task PlayTrackAsync(string handle);

    /// <summary>Pauses a track without discarding its position. No-op if unknown.</summary>
    Task PauseTrackAsync(string handle);

    /// <summary>Seeks a track to an absolute position, in seconds. No-op if unknown.</summary>
    Task SeekTrackAsync(string handle, double seconds);

    /// <summary>Current playback status of a track — all-default (not playing, 0/0) if unknown or not yet loaded.</summary>
    Task<TrackStatus> GetTrackStatusAsync(string handle);

    /// <summary>Releases a track's resources — call when its <c>audio_track</c> element unmounts. No-op if unknown.</summary>
    Task DisposeTrackAsync(string handle);

    /// <summary>
    /// Registers a callback invoked once, via JS interop, when the track identified by
    /// <paramref name="handle"/> reaches its natural end — not a manual <see cref="PauseTrackAsync"/>.
    /// <paramref name="callbackMethodName"/> must name a public <c>[JSInvokable]</c> no-argument
    /// method on <paramref name="callbackTarget"/>'s wrapped instance.
    /// </summary>
    Task SetTrackEndedCallbackAsync<T>(string handle, DotNetObjectReference<T> callbackTarget, string callbackMethodName) where T : class;
}

/// <summary>Snapshot of an addressable <c>audio_track</c>'s playback state — see <see cref="IAudioPlayer.GetTrackStatusAsync"/>.</summary>
/// <param name="IsPlaying">Whether the track is currently playing (not paused, not ended).</param>
/// <param name="PositionSeconds">Current playback position, in seconds.</param>
/// <param name="DurationSeconds">Total track duration, in seconds — <c>0</c> if not yet known (e.g. metadata hasn't loaded).</param>
public sealed record TrackStatus(bool IsPlaying, double PositionSeconds, double DurationSeconds);
