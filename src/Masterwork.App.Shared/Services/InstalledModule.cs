namespace Masterwork.App.Shared.Services;

/// <summary>
/// One entry in <see cref="IModuleStore"/>'s installed-module index — enough metadata for the
/// Start New Game carousel and Manage Modules screen without needing to load the module's full
/// content. Real manifest-driven metadata (dependencies, licensing, etc.) arrives with the
/// <c>.mwm</c> package format in Phase 3 Milestone B; for now this is a thin, hand-populated stand-in.
/// </summary>
/// <param name="Sha256">
/// The installed package's SHA256 (see <see cref="ModuleHasher"/>), computed once at install time and
/// persisted here so a same-version re-upload only ever needs to hash the new upload, never re-fetch
/// and re-hash the already-installed copy.
/// </param>
/// <param name="ThumbnailImageUrl">
/// The module's <c>thumbnail.image</c> (see <see cref="Masterwork.ModuleFormat.ModuleThumbnail"/>),
/// already resolved to a displayable URL by <see cref="ModuleThumbnailResolver"/> — or
/// <see langword="null"/> if the module declares none. The carousel tile's own border/selection
/// chrome is theme-owned CSS, not sourced from the module, so only the raw thumbnail art travels
/// through here — see <c>ModuleThumbnail.BorderInactive</c>/<c>BorderActive</c>'s own remarks.
/// </param>
public sealed record InstalledModule(
    string ModuleId,
    string Version,
    string Title,
    string Description,
    IReadOnlyList<string> AvailableLanguages,
    string Sha256,
    string? ThumbnailImageUrl = null
);
