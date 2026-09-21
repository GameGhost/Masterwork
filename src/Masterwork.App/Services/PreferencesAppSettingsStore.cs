using Masterwork.App.Shared.Services;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IAppSettingsStore"/>
/// <remarks>
/// Backed by <see cref="Preferences"/> — MAUI-only, lives in the MAUI head rather than the
/// platform-agnostic Shared project.
///
/// Stores the whole record as one JSON value through <see cref="AppSettingsJson"/>, the same path the
/// web store uses, so every setting persists here as soon as it exists. This store used to write a
/// hand-maintained list of fields instead, and silently dropped any setting nobody remembered to add —
/// which is how hidden catalog listings, added sources, and trusted publishers all failed to survive a
/// reload on native heads while working on the web.
/// </remarks>
public sealed class PreferencesAppSettingsStore : IAppSettingsStore
{
    private const string SettingsKey = "settings.json";

    // Written by the old field-by-field store. Read once, to carry an existing player's choices over
    // into the new single value; never written again.
    private const string LegacyBgmVolumeKey = "settings.bgmVolume";
    private const string LegacyBgmMutedKey = "settings.bgmMuted";
    private const string LegacySfxVolumeKey = "settings.sfxVolume";
    private const string LegacySfxMutedKey = "settings.sfxMuted";
    private const string LegacyTextSizeStepKey = "settings.textSizeStep";
    private const string LegacyUiLocaleKey = "settings.uiLocale";
    private const string LegacyPreferredModuleLanguageKey = "settings.preferredModuleLanguage";

    /// <summary>
    /// Sets thread culture from the saved <c>UiLocale</c> preference — call this as the very first
    /// thing in <c>MauiProgram.CreateMauiApp()</c>, before the host is built and the first render
    /// happens. <see cref="Preferences"/> is synchronous, unlike <see cref="LoadAsync"/>'s async
    /// contract, which is what makes this possible: without it, the main view always paints once in
    /// English regardless of what's saved.
    /// </summary>
    public static void ApplyStartupCulture() => AppSettingsApplier.ApplyCulture(Read().UiLocale);

    /// <inheritdoc/>
    public Task<AppSettings> LoadAsync() => Task.FromResult(Read());

    /// <inheritdoc/>
    public Task SaveAsync(AppSettings settings)
    {
        Preferences.Set(SettingsKey, AppSettingsJson.Serialize(settings));
        return Task.CompletedTask;
    }

    private static AppSettings Read()
    {
        var json = Preferences.Get(SettingsKey, string.Empty);
        return string.IsNullOrEmpty(json) ? ReadLegacy() : AppSettingsJson.Deserialize(json);
    }

    // Only reached before the first save under the new key, so a player upgrading keeps their volume,
    // text size, and language rather than finding them reset. Everything the old store never wrote
    // takes its default, which is exactly what that player had.
    private static AppSettings ReadLegacy()
    {
        var defaults = AppSettings.Default;
        var preferredModuleLanguage = Preferences.Get(LegacyPreferredModuleLanguageKey, string.Empty);

        return new AppSettings
        {
            BgmVolume = Preferences.Get(LegacyBgmVolumeKey, defaults.BgmVolume),
            BgmMuted = Preferences.Get(LegacyBgmMutedKey, defaults.BgmMuted),
            SfxVolume = Preferences.Get(LegacySfxVolumeKey, defaults.SfxVolume),
            SfxMuted = Preferences.Get(LegacySfxMutedKey, defaults.SfxMuted),
            TextSizeStep = Preferences.Get(LegacyTextSizeStepKey, defaults.TextSizeStep),
            UiLocale = Preferences.Get(LegacyUiLocaleKey, defaults.UiLocale),
            PreferredModuleLanguage = string.IsNullOrEmpty(preferredModuleLanguage) ? null : preferredModuleLanguage,
        };
    }
}
