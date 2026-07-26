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
public sealed record InstalledModule(
    string ModuleId,
    string Version,
    string Title,
    string Description,
    IReadOnlyList<string> AvailableLanguages,
    string Sha256
);
