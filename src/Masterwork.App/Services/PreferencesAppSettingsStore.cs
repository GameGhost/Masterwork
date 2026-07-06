using Masterwork.App.Shared.Services;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IAppSettingsStore"/>
/// <remarks>Backed by <see cref="Preferences"/> — MAUI-only, lives in the MAUI head rather than the platform-agnostic Shared project.</remarks>
public sealed class PreferencesAppSettingsStore : IAppSettingsStore
{
    private const string BgmVolumeKey = "settings.bgmVolume";
    private const string BgmMutedKey = "settings.bgmMuted";
    private const string SfxVolumeKey = "settings.sfxVolume";
    private const string SfxMutedKey = "settings.sfxMuted";
    private const string TextSizeStepKey = "settings.textSizeStep";

    /// <inheritdoc/>
    public Task<AppSettings> LoadAsync()
    {
        var defaults = AppSettings.Default;
        var settings = new AppSettings
        {
            BgmVolume = Preferences.Get(BgmVolumeKey, defaults.BgmVolume),
            BgmMuted = Preferences.Get(BgmMutedKey, defaults.BgmMuted),
            SfxVolume = Preferences.Get(SfxVolumeKey, defaults.SfxVolume),
            SfxMuted = Preferences.Get(SfxMutedKey, defaults.SfxMuted),
            TextSizeStep = Preferences.Get(TextSizeStepKey, defaults.TextSizeStep),
        };
        return Task.FromResult(settings);
    }

    /// <inheritdoc/>
    public Task SaveAsync(AppSettings settings)
    {
        Preferences.Set(BgmVolumeKey, settings.BgmVolume);
        Preferences.Set(BgmMutedKey, settings.BgmMuted);
        Preferences.Set(SfxVolumeKey, settings.SfxVolume);
        Preferences.Set(SfxMutedKey, settings.SfxMuted);
        Preferences.Set(TextSizeStepKey, settings.TextSizeStep);
        return Task.CompletedTask;
    }
}
