namespace Masterwork.App.Shared.Services;

/// <summary>
/// The currently-live volume/mute settings — mirrors whatever's actually been pushed to
/// <see cref="IAudioPlayer"/> most recently, whether that's the last-saved <see cref="AppSettings"/>
/// (applied once at startup) or an in-progress, not-yet-saved edit in the Options dialog (applied
/// live as the player drags a slider, so they can hear the effect immediately — see
/// <c>OptionsDialog.razor</c>). Scoped the same way as <see cref="GameSessionState"/>, so it reflects
/// one app session's worth of live state, not a single component's.
/// </summary>
/// <remarks>
/// Exists so components that need to react to the *current* mute state — e.g.
/// <see cref="Rendering.RenderedAudioTrackView"/> disabling/pausing itself while SFX is muted, since
/// <c>audio_track</c> playback shares the SFX volume channel — don't need their own
/// <see cref="IAudioPlayer"/> round trip just to ask "is it muted right now." Kept to the four audio
/// fields only; text size and UI locale have no live-preview requirement and stay
/// <see cref="OptionsDialog.razor"/>-local until Apply.
/// </remarks>
public sealed class AudioSettingsState
{
    public double BgmVolume { get; private set; } = AppSettings.Default.BgmVolume;
    public bool BgmMuted { get; private set; } = AppSettings.Default.BgmMuted;
    public double SfxVolume { get; private set; } = AppSettings.Default.SfxVolume;
    public bool SfxMuted { get; private set; } = AppSettings.Default.SfxMuted;

    /// <summary>Updates every field at once — always all four together, since they're pushed to <see cref="IAudioPlayer"/> as a unit (see <see cref="AppSettingsApplier.ApplyAudioAsync"/>).</summary>
    public void Update(double bgmVolume, bool bgmMuted, double sfxVolume, bool sfxMuted)
    {
        BgmVolume = bgmVolume;
        BgmMuted = bgmMuted;
        SfxVolume = sfxVolume;
        SfxMuted = sfxMuted;
    }
}
