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

    /// <summary>
    /// The app shell's own display language — a culture code from <see cref="AppUiCultures.Supported"/>.
    /// Distinct from <see cref="PreferredModuleLanguage"/>: this is chrome only (Milestone A.1),
    /// never module content. Applied via <see cref="AppSettingsApplier"/>, which sets the generated
    /// <c>AppStrings.CultureInfo</c> static property (see <c>Resources/AppStrings.resx</c>) — never
    /// <c>CultureInfo.CurrentUICulture</c>. Options' Apply then force-navigates back to the main view
    /// so the already-rendered tree picks up the new culture immediately.
    /// </summary>
    public string UiLocale { get; init; } = AppUiCultures.Default;

    /// <summary>
    /// The player's preferred language for module *content* — independent of <see cref="UiLocale"/>.
    /// <see langword="null"/> means "no preference, use whatever the module declares as its own
    /// default." Only takes effect for modules that actually have that language (Section 11's
    /// fallback chain); a module without it just falls back silently.
    /// </summary>
    public string? PreferredModuleLanguage { get; init; }

    /// <summary>The default settings, used until the player changes and saves anything.</summary>
    public static readonly AppSettings Default = new();
}

/// <summary>
/// The app shell's own supported UI languages (Milestone A.1) — a short, hand-maintained list, since
/// unlike module content (which can add a locale just by shipping another <c>.restext</c> file) a
/// new app-shell language needs an actual <c>.resx</c> satellite resource added to the build.
/// </summary>
public static class AppUiCultures
{
    public const string Default = "en-US";

    /// <summary>Culture code → display name, in the order shown in the Options dropdown.</summary>
    public static readonly IReadOnlyList<(string Code, string DisplayName)> Supported =
    [
        (Default, "English"),
        ("fr-CA", "Français (Canada)"),
    ];
}
