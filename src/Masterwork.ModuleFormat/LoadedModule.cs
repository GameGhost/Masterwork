namespace Masterwork.ModuleFormat;

/// <summary>
/// A fully loaded module: every passage parsed and restext-resolved, the variable manifest, the
/// locale dictionary, and any warnings accumulated while loading. Built by
/// <see cref="Masterwork.ModuleFormat.ModuleLoader"/>.
/// </summary>
/// <remarks>
/// A record (not a plain class) specifically so <c>LoadedModuleContent.BuildAsync</c> (Masterwork.App.Shared)
/// can produce a copy with <see cref="StartPassageId"/> overridden via <c>with</c> — the least
/// invasive way to let <c>manifest.yaml</c>'s <c>entry:</c> field (see <see cref="ModuleManifest.Entry"/>)
/// take priority over the <c>Begins-Here</c> tag scan below without threading manifest data through
/// <see cref="ModuleLoader"/>'s own multiple call sites.
/// </remarks>
public sealed record LoadedModule
{
    /// <summary>All passages, keyed by <c>passage_id</c>.</summary>
    public required IReadOnlyDictionary<string, MwsPassageDoc> Passages { get; init; }

    /// <summary>Session and standard variable definitions, keyed by name.</summary>
    public required IReadOnlyDictionary<string, VarDef> Variables { get; init; }

    /// <summary>The <c>en-US.restext</c> locale dictionary, keyed by restext key.</summary>
    public required IReadOnlyDictionary<string, string> Locale { get; init; }

    /// <summary>Issues recorded while loading — missing fields, unresolved references, unknown node types, etc.</summary>
    public required ModuleWarnings Warnings { get; init; }

    /// <summary>The passage tagged <c>Begins-Here</c> (case-insensitive), or <see langword="null"/> if none is tagged.</summary>
    public string? StartPassageId { get; init; }

    /// <summary>Module-authored layout chrome (header/footer/before/after regions), keyed by layout id.</summary>
    public required IReadOnlyDictionary<string, LayoutChromeDoc> LayoutChrome { get; init; }

    /// <summary>
    /// The manifest's <c>audio:</c> section (module-level default music/SFX), if declared. Threaded
    /// through from <c>manifest.yaml</c> the same way <see cref="StartPassageId"/> is overridden by
    /// <c>entry:</c> — see <c>LoadedModuleContent.BuildAsync</c> (Masterwork.App.Shared).
    /// </summary>
    public ModuleAudioManifest? Audio { get; init; }
}
