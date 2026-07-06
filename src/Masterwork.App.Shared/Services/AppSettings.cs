namespace Masterwork.App.Shared.Services;

/// <summary>
/// Truly-global, app-shell-level settings — the Options dialog's contents (Section 13). Persisted
/// via <see cref="IAppSettingsStore"/> and applied to <see cref="IAudioPlayer"/> and the app's
/// text-size CSS custom property wherever they're read.
/// </summary>
public sealed record AppSettings
{
    /// <summary>0.0-1.0 background/ambient music volume.</summary>
    public double BgmVolume { get; init; } = 1.0;

    /// <summary>Whether background music is muted, independent of <see cref="BgmVolume"/>.</summary>
    public bool BgmMuted { get; init; }

    /// <summary>0.0-1.0 sound effect volume.</summary>
    public double SfxVolume { get; init; } = 1.0;

    /// <summary>Whether sound effects are muted, independent of <see cref="SfxVolume"/>.</summary>
    public bool SfxMuted { get; init; }

    /// <summary>Text size step, 0-5 (6 fixed stops) — 2 is the default/"normal" size.</summary>
    public int TextSizeStep { get; init; } = 2;

    /// <summary>The default settings, used until the player changes and saves anything.</summary>
    public static readonly AppSettings Default = new();
}
