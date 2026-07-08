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
    private const string UiLocaleKey = "settings.uiLocale";
    private const string PreferredModuleLanguageKey = "settings.preferredModuleLanguage";

    /// <summary>
    /// Sets thread culture from the saved <c>UiLocale</c> preference — call this as the very first
    /// thing in <c>MauiProgram.CreateMauiApp()</c>, before the host is built and the first render
    /// happens. <see cref="Preferences"/> is synchronous, unlike <see cref="LoadAsync"/>'s async
    /// contract, which is what makes this possible: without it, the main view always paints once in
    /// English regardless of what's saved (see masterwork-plan-rev17.md).
    /// </summary>
    public static void ApplyStartupCulture() =>
        AppSettingsApplier.ApplyCulture(Preferences.Get(UiLocaleKey, AppSettings.Default.UiLocale));

    /// <inheritdoc/>
    public Task<AppSettings> LoadAsync()
    {
        var defaults = AppSettings.Default;
        var preferredModuleLanguage = Preferences.Get(PreferredModuleLanguageKey, string.Empty);
        var settings = new AppSettings
        {
            BgmVolume = Preferences.Get(BgmVolumeKey, defaults.BgmVolume),
            BgmMuted = Preferences.Get(BgmMutedKey, defaults.BgmMuted),
            SfxVolume = Preferences.Get(SfxVolumeKey, defaults.SfxVolume),
            SfxMuted = Preferences.Get(SfxMutedKey, defaults.SfxMuted),
            TextSizeStep = Preferences.Get(TextSizeStepKey, defaults.TextSizeStep),
            UiLocale = Preferences.Get(UiLocaleKey, defaults.UiLocale),
            PreferredModuleLanguage = string.IsNullOrEmpty(preferredModuleLanguage) ? null : preferredModuleLanguage,
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
        Preferences.Set(UiLocaleKey, settings.UiLocale);
        Preferences.Set(PreferredModuleLanguageKey, settings.PreferredModuleLanguage ?? string.Empty);
        return Task.CompletedTask;
    }
}
