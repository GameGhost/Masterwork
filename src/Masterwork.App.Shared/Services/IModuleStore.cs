using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The app's installed-module registry, backing the Start New Game carousel and Manage Modules
/// screen. <see cref="EmbeddedModuleStore"/> is a Milestone A stand-in that only ever returns the
/// built-in demo module — real <c>.mwm</c> upload/download and per-host persistent storage
/// (IndexedDB on Web, app-data directory on MAUI) are Phase 3 Milestone B work.
/// </summary>
public interface IModuleStore
{
    /// <summary>All modules currently installed, built-in or otherwise.</summary>
    Task<IReadOnlyList<InstalledModule>> ListAsync();

    /// <summary>Loads the full content of an installed module by id.</summary>
    Task<LoadedModule> LoadAsync(string moduleId);
}
