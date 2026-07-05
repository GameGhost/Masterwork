namespace Masterwork.App.Shared.Services;

/// <summary>
/// Plays background music (with crossfade), one-shot sound effects, and reduces volume to an
/// "ambient" level while the timeline is rewound. One implementation
/// (<see cref="JsAudioPlayer"/>) works on every host — MAUI's <c>BlazorWebView</c> is a real
/// WebView, so plain HTML5 audio + the Web Audio API (via <see cref="Microsoft.JSInterop.IJSRuntime"/>)
/// behave the same there as in a browser, unlike storage (<see cref="ISaveStore"/>), which
/// genuinely needs different native APIs per host.
/// </summary>
public interface IAudioPlayer
{
    /// <summary>
    /// Starts (or crossfades to) a looping background track. <paramref name="url"/> may be a real
    /// audio file URL, or the special <c>synth://tone</c> value, which synthesizes a sustained test
    /// tone instead of requiring a real asset — there's no real BGM content until Phase 3.
    /// </summary>
    Task PlayBgmAsync(string? url);

    /// <summary>Fades out and stops the current background track, if any.</summary>
    Task StopBgmAsync();

    /// <summary>
    /// Plays a one-shot sound effect. <paramref name="url"/> may be a real audio file URL, or the
    /// special <c>synth://blip</c> value for a synthesized test blip.
    /// </summary>
    Task PlaySfxAsync(string? url);

    /// <summary>Reduces (or restores) background music volume to an "ambient" level — wired to <see cref="Masterwork.Engine.GameSession.IsRewound"/>.</summary>
    Task SetAmbientOnlyAsync(bool ambientOnly);
}
