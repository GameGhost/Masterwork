namespace Masterwork.App.Shared.Services;

/// <summary>
/// Persists serialized <see cref="Masterwork.Engine.Session.SessionSave"/> JSON, keyed by save id
/// (see <see cref="SaveIds"/>), alongside a listable <see cref="SaveEntry"/> metadata header per
/// save so the Continue List can render without deserializing every save's full timeline.
/// Implemented per host — <see cref="LocalStorageSaveStore"/> for the web (browser
/// <c>localStorage</c>), and a MAUI-specific <c>FileSaveStore</c> (in the <c>Masterwork.App</c>
/// head, since it needs MAUI-only filesystem/share APIs not available to this platform-agnostic
/// project) for the native heads.
/// </summary>
public interface ISaveStore
{
    /// <summary>All saves currently known, across every module.</summary>
    Task<IReadOnlyList<SaveEntry>> ListAsync();

    /// <summary>Reads a save's JSON, or <see langword="null"/> if <paramref name="id"/> doesn't exist.</summary>
    Task<string?> LoadAsync(string id);

    /// <summary>Writes (or overwrites) a save's JSON and its metadata entry as a single upsert.</summary>
    Task SaveAsync(SaveEntry entry, string json);

    /// <summary>Removes a save and its metadata entry. A no-op if it doesn't exist.</summary>
    Task DeleteAsync(string id);

    /// <summary>Exports a save's JSON as a shareable/downloadable file named <paramref name="fileName"/>.</summary>
    Task ExportAsync(string id, string fileName, string json);
}
