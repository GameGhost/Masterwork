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

    /// <summary>
    /// SHA-256 thumbprints of content publishers the player chose to trust going forward, from the
    /// unrecognized-signer prompt during a manual install (see <see cref="PackageTrustEvaluator"/>).
    /// Per-device, like every other setting here — trusting a publisher on one device says nothing
    /// about another. Distinct from <see cref="WhiteLabelConfig.PublisherThumbprint"/>, which is the build's own
    /// publisher and needs no player decision at all.
    /// </summary>
    public IReadOnlyList<string> TrustedPublisherThumbprints { get; init; } = [];

    /// <summary>
    /// Catalog URLs the player subscribed to themselves, in the order they added them. The build's
    /// own source (<see cref="WhiteLabelConfig.CatalogUrl"/>) is never in this list — it can't be
    /// removed, and it always takes precedence over these when two sources publish the same id.
    /// </summary>
    public IReadOnlyList<string> CustomCatalogSources { get; init; } = [];

    /// <summary>
    /// Catalog listings the player chose to hide, keyed by source *and* module — the same module
    /// published by two sources is two listings, and hiding one says nothing about the other.
    ///
    /// Hiding is therefore more than a display preference: because sources resolve in subscription
    /// order, hiding a module's listing on an earlier source lets a later source's listing of the
    /// same module take its place. That's how a player opts one module over to a pre-release source
    /// while everything else stays on the stable one.
    ///
    /// Never an uninstall, and never applied to installed content: an installed module always
    /// appears on the New Game page whatever is hidden, because hiding a listing says where updates
    /// come from, not whether something can be played.
    /// </summary>
    public IReadOnlyList<HiddenCatalogListing> HiddenCatalogListings { get; init; } = [];

    /// <summary>The default settings, used until the player changes and saves anything.</summary>
    public static readonly AppSettings Default = new();
}

/// <summary>One source's listing of one module, hidden by the player. See <see cref="AppSettings.HiddenCatalogListings"/>.</summary>
/// <param name="SourceKey">
/// Which source's listing — <see cref="CatalogSourceKeys.Primary"/> for the build's own source, or
/// the catalog URL for one the player added. The built-in source is keyed by a constant rather than
/// by its URL deliberately: on the web head that URL is the deployment's own origin, so keying by it
/// would silently drop every hidden listing when the app moves between, say, localhost and its real
/// address.
/// </param>
/// <param name="ModuleId">The module whose listing is hidden.</param>
public sealed record HiddenCatalogListing(string SourceKey, string ModuleId);

/// <summary>How a catalog source is identified in stored settings.</summary>
public static class CatalogSourceKeys
{
    /// <summary>The build's own source — singular per build, so it needs no URL to identify it.</summary>
    public const string Primary = "primary";
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
