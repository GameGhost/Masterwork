namespace Masterwork.App.Shared.Services;

/// <summary>
/// Persists serialized <see cref="Masterwork.Engine.Session.SessionSave"/> JSON to local storage,
/// keyed by slot number. Implemented per host — <see cref="LocalStorageSaveStore"/> for the web
/// (browser <c>localStorage</c>), and a MAUI-specific <c>FileSaveStore</c> (in the
/// <c>Masterwork.App</c> head, since it needs MAUI-only filesystem/share APIs not available to
/// this platform-agnostic project) for the native heads.
/// </summary>
public interface ISaveStore
{
    /// <summary>Writes <paramref name="json"/> to the given slot, overwriting any existing save there.</summary>
    Task SaveAsync(int slot, string json);

    /// <summary>Reads the given slot's JSON, or <see langword="null"/> if nothing is saved there.</summary>
    Task<string?> LoadAsync(int slot);

    /// <summary>Whether the given slot has a save.</summary>
    Task<bool> HasSaveAsync(int slot);

    /// <summary>Exports a slot's JSON as a shareable/downloadable file named <paramref name="fileName"/>.</summary>
    Task ExportAsync(int slot, string fileName, string json);
}
