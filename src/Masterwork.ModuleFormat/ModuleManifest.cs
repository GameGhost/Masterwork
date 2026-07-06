namespace Masterwork.ModuleFormat;

/// <summary>A dependency declaration from a module's <c>manifest.yaml</c> — e.g. an asset pack the module builds on.</summary>
public sealed record ModuleDependency
{
    /// <summary>The dependency's own <see cref="ModuleManifest.Id"/>.</summary>
    public required string Id { get; init; }

    /// <summary>A version requirement string (e.g. <c>">=1.0.0"</c>). Not yet enforced — resolution just matches by id (see Milestone B).</summary>
    public string? Version { get; init; }
}

/// <summary>
/// A module or asset pack's <c>manifest.yaml</c> — identity, versioning, and dependency
/// declarations. Distinct from <c>_variables.yaml</c> (variable declarations) and the passage
/// <c>*.mws.yaml</c> files, which keep their own established file layout.
/// </summary>
public sealed record ModuleManifest
{
    /// <summary>Stable identifier, e.g. <c>"original.cost_of_disease"</c> or <c>"MFW_Common_Assets"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display title.</summary>
    public required string Title { get; init; }

    /// <summary>Semver-ish version string.</summary>
    public required string Version { get; init; }

    /// <summary>One of the module types from masterwork-plan's Section 8 (<c>original_scenario</c>, <c>asset_pack</c>, etc.). Defaults to <c>original_scenario</c>.</summary>
    public string ModuleType { get; init; } = "original_scenario";

    /// <summary>Human-readable description, shown in the Start New Game module detail panel.</summary>
    public string? Description { get; init; }

    /// <summary>Other modules (typically asset packs) this module depends on.</summary>
    public IReadOnlyList<ModuleDependency> Dependencies { get; init; } = [];
}
