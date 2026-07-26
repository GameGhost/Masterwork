using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The app's installed-module registry, backing the Start New Game carousel and Manage Modules
/// screen. <see cref="IndexedDbModuleStore"/> (web, backed by IndexedDB rather than
/// <c>localStorage</c>, since real packages with real assets are multi-megabyte) and a MAUI-specific
/// <c>FileModuleStore</c> (in the <c>Masterwork.App</c> head) implement this for their respective
/// hosts — neither includes any built-in/permanent module; every entry is a real uploaded package.
/// </summary>
public interface IModuleStore
{
    /// <summary>All modules currently installed.</summary>
    Task<IReadOnlyList<InstalledModule>> ListAsync();

    /// <summary>
    /// Loads the full content of an installed module by id, resolving its passage text against
    /// <paramref name="locale"/> if given (see <see cref="Masterwork.ModuleFormat.ModuleLocales"/> for
    /// the fallback chain — falls through to the module's own default, then whatever's available, if
    /// <paramref name="locale"/> is <see langword="null"/> or the module doesn't have it). Also
    /// returns an <see cref="IModuleAssetSource"/> and stylesheet text (see
    /// <see cref="LoadedModuleContent"/>) — the caller publishes these into
    /// <see cref="GameSessionState"/> so <see cref="IAssetResolver"/> and the module-CSS injection can
    /// use them for the lifetime of the session.
    /// </summary>
    Task<LoadedModuleContent> LoadAsync(string moduleId, string? locale = null);

    /// <summary>
    /// Installs a <c>.mwm</c> package from raw bytes (a browser upload or a MAUI file read), keyed by
    /// its manifest's declared id. <paramref name="sha256"/> is the package's own SHA256 (see
    /// <see cref="ModuleHasher"/>) — computed once by the caller (during upload processing, with its
    /// own progress feedback) and persisted here rather than re-hashed a second time during install.
    /// <paramref name="progress"/>, if given, is reported (done count, total count) as the package's
    /// entries are persisted — install streams entries directly into storage rather than holding the
    /// whole decompressed package in memory at once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The package has no <c>manifest.yaml</c>.</exception>
    Task<InstalledModule> InstallAsync(byte[] mwmBytes, string sha256, IProgress<(int Done, int Total)>? progress = null);

    /// <summary>Uninstalls a module. Does not touch its saves — see Manage Modules for that prompt.</summary>
    Task DeleteAsync(string moduleId);
}
