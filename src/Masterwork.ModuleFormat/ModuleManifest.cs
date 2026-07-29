namespace Masterwork.ModuleFormat;

/// <summary>A dependency declaration from a module's <c>manifest.yaml</c> — e.g. an asset pack the module builds on.</summary>
public sealed record ModuleDependency
{
    /// <summary>The dependency's own <see cref="ModuleManifest.Id"/>.</summary>
    public required string Id { get; init; }

    /// <summary>A version requirement string (e.g. <c>">=1.0.0"</c>). Not yet enforced — resolution just matches by id (see Milestone B).</summary>
    public string? Version { get; init; }
}

/// <summary>Module-select thumbnail image/border asset refs (<c>image://</c> URIs), if declared.</summary>
public sealed record ModuleThumbnail
{
    /// <summary>
    /// The thumbnail artwork itself — the only field the app's default rendering currently consumes
    /// (see <c>Masterwork.App.Shared.Services.ModuleThumbnailResolver</c>).
    /// </summary>
    public string? Image { get; init; }

    /// <summary>
    /// Border shown when the module is not the currently-selected one. Kept in the format for a
    /// module that wants to override the theme's own default tile border, but the app's default
    /// carousel rendering doesn't read this yet — that border is theme-owned CSS.
    /// </summary>
    public string? BorderInactive { get; init; }

    /// <summary>Border shown when the module is the currently-selected one. See <see cref="BorderInactive"/>'s remarks.</summary>
    public string? BorderActive { get; init; }
}

/// <summary>Player count / playtime info shown in the Start New Game module detail panel, if declared.</summary>
public sealed record ModuleInfo
{
    /// <summary>Minimum supported player count.</summary>
    public int? PlayersMin { get; init; }

    /// <summary>Maximum supported player count.</summary>
    public int? PlayersMax { get; init; }

    /// <summary>Human-readable playtime estimate, resolved to the requested/default locale (e.g. <c>"180 minutes"</c>).</summary>
    public string? Playtime { get; init; }
}

/// <summary>
/// A module or asset pack's <c>manifest.yaml</c> — identity, versioning, dependency, and
/// display/UI declarations. Distinct from <c>_variables.yaml</c> (variable declarations) and the
/// passage <c>*.mws.yaml</c> files, which keep their own established file layout.
/// </summary>
public sealed record ModuleManifest
{
    /// <summary>Stable identifier, e.g. <c>"original.cost_of_disease"</c> or <c>"MFW_Common_Assets"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display title, resolved to the requested/default locale. <c>title:</c> may be a plain string
    /// or a localized list (<c>- en-US: 'The Cost of Disease'</c>); see <see cref="ManifestParser.Parse"/>'s
    /// <c>preferredLocale</c> parameter.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>Semver-ish version string.</summary>
    public required string Version { get; init; }

    /// <summary>One of the module types from masterwork-plan's Section 8 (<c>original_scenario</c>, <c>asset_pack</c>, etc.). Defaults to <c>original_scenario</c>.</summary>
    public string ModuleType { get; init; } = "original_scenario";

    /// <summary>Human-readable description, resolved to the requested/default locale, shown in the Start New Game module detail panel. <c>description:</c> may be a plain string or a localized list, same as <see cref="Title"/>.</summary>
    public string? Description { get; init; }

    /// <summary>Other modules (typically asset packs) this module depends on.</summary>
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = [];

    /// <summary>Content languages this module ships restext for (from the top-level <c>languages:</c> list). Empty if not declared.</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>Module-select thumbnail, if declared.</summary>
    public ModuleThumbnail? Thumbnail { get; init; }

    /// <summary>Player count / playtime info, if declared.</summary>
    public ModuleInfo? Info { get; init; }

    /// <summary>
    /// Entry passage id (masterwork-plan §9), if declared. Not yet consumed by
    /// <see cref="ModuleLoader"/>, which still resolves the start passage via the
    /// <c>Begins-Here</c> tag — exposed here for callers (e.g. the future Editor) that want to
    /// show or validate the declared entry point.
    /// </summary>
    public string? Entry { get; init; }

    /// <summary>Relative path to the extractor-owned passages folder. Defaults to <c>"passages"</c> — see <see cref="ModuleLoader.LoadFromDirectory"/>, which resolves this independently with its own tolerant fallback and does not consume this parsed value directly.</summary>
    public string PassagesPath { get; init; } = "passages";

    /// <summary>Relative path to the hand-maintained passage-overrides folder. Defaults to <c>"passages-override"</c> — see the note on <see cref="PassagesPath"/>.</summary>
    public string PassagesOverridePath { get; init; } = "passages-override";

    /// <summary>
    /// Relative path to this module's stylesheet, applied by the app at module-load time. The
    /// chrome (<c>Masterwork.App.Shared</c>'s Razor components) only ever emits structural class
    /// hooks (<c>layout-{value}</c>, <c>style-{value}</c>) — all layout/style-specific visual
    /// treatment comes from here, not from the app's own CSS. Defaults to <c>"assets/style.css"</c>;
    /// a module with no stylesheet at that path simply gets no custom styling (unstyled structure).
    /// </summary>
    public string StylePath { get; init; } = "assets/style.css";
}
