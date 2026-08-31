namespace Masterwork.ModuleFormat;

/// <summary>
/// A <c>.mwassets</c> package's <c>manifest.yaml</c> — identity/versioning only. Unlike
/// <see cref="ModuleManifest"/>, <see cref="Title"/>/<see cref="Description"/> are plain strings, not
/// localized lists, since they're for display in module-management UI, not player-facing content.
/// </summary>
public sealed record AssetPackManifest
{
    /// <summary>Stable identifier, e.g. <c>"MFW_Common_Assets"</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Display title, plain text.</summary>
    public required string Title { get; init; }

    /// <summary>Human-readable description, plain text.</summary>
    public string? Description { get; init; }

    /// <summary>Semver-ish version string — a dependent module pins one exact value of this (see <see cref="ModuleDependency"/>).</summary>
    public required string Version { get; init; }

    /// <summary>
    /// This asset pack's own default content locale — the fallback target for its own restext keys
    /// when a dependent module resolves to a locale the pack doesn't have. Independent of any
    /// dependent module's own default locale. Defaults to <see cref="ModuleLocales.Default"/> (<c>en-US</c>).
    /// </summary>
    public string DefaultLocale { get; init; } = ModuleLocales.Default;
}
