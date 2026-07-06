namespace Masterwork.App.Shared.Services;

/// <summary>
/// Persists the single global <see cref="AppSettings"/> record. Implemented per host —
/// <see cref="LocalStorageAppSettingsStore"/> for the web (browser <c>localStorage</c>), and a
/// MAUI-specific <c>PreferencesAppSettingsStore</c> (in the <c>Masterwork.App</c> head, using
/// <c>Microsoft.Maui.Storage.Preferences</c>) for the native heads.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>Reads the current settings, or <see cref="AppSettings.Default"/> if nothing has been saved yet.</summary>
    Task<AppSettings> LoadAsync();

    /// <summary>Persists <paramref name="settings"/>, overwriting whatever was there before.</summary>
    Task SaveAsync(AppSettings settings);
}
