using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The app's installed-module registry, backing the Start New Game carousel and Manage Modules
/// screen. Always includes <see cref="BuiltInModules.Demo"/> alongside whatever <c>.mwm</c>
/// packages have been uploaded — <see cref="IndexedDbModuleStore"/> (web, backed by IndexedDB
/// rather than <c>localStorage</c>, since real packages with real assets are multi-megabyte) and a
/// MAUI-specific <c>FileModuleStore</c> (in the <c>Masterwork.App</c> head) for the native heads.
/// </summary>
public interface IModuleStore
{
    /// <summary>All modules currently installed, built-in or otherwise.</summary>
    Task<IReadOnlyList<InstalledModule>> ListAsync();

    /// <summary>Loads the full content of an installed module by id.</summary>
    Task<LoadedModule> LoadAsync(string moduleId);

    /// <summary>Installs a <c>.mwm</c> package from raw bytes (a browser upload or a MAUI file read), keyed by its manifest's declared id.</summary>
    /// <exception cref="InvalidOperationException">The package has no <c>manifest.yaml</c>.</exception>
    Task<InstalledModule> InstallAsync(byte[] mwmBytes);

    /// <summary>Reads back the raw <c>.mwm</c> bytes for an installed, non-built-in module — used to detect whether a re-upload is actually identical content. Returns <see langword="null"/> for the built-in module or an unknown id.</summary>
    Task<byte[]?> GetPackageBytesAsync(string moduleId);

    /// <summary>Uninstalls a module. Does not touch its saves — see Manage Modules for that prompt.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="moduleId"/> is a built-in module.</exception>
    Task DeleteAsync(string moduleId);
}
