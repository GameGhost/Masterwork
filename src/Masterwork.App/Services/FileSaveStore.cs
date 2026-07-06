using System.Text.Json;
using Masterwork.App.Shared.Services;

namespace Masterwork.App.Services;

/// <inheritdoc cref="ISaveStore"/>
/// <remarks>
/// Backed by <see cref="FileSystem"/>'s app data directory and the MAUI <see cref="Share"/> API for
/// export. MAUI-only — lives in the MAUI head rather than the platform-agnostic Shared project. The
/// full list of <see cref="SaveEntry"/> metadata is kept as one JSON array file (<see cref="IndexPath"/>),
/// mirroring <see cref="LocalStorageSaveStore"/>'s index-plus-per-id-blob layout.
/// </remarks>
public sealed class FileSaveStore : ISaveStore
{
    private static string IndexPath => Path.Combine(FileSystem.AppDataDirectory, "saves-index.json");

    private static string DataPath(string id) => Path.Combine(FileSystem.AppDataDirectory, $"save-{Sanitize(id)}.json");

    private static string Sanitize(string id) =>
        new(id.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SaveEntry>> ListAsync() => await ReadIndexAsync();

    /// <inheritdoc/>
    public async Task<string?> LoadAsync(string id)
    {
        var path = DataPath(id);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(SaveEntry entry, string json)
    {
        await File.WriteAllTextAsync(DataPath(entry.Id), json);
        var index = await ReadIndexAsync();
        var updated = index.Where(e => e.Id != entry.Id).Append(entry).ToList();
        await WriteIndexAsync(updated);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string id)
    {
        var path = DataPath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var index = await ReadIndexAsync();
        await WriteIndexAsync([.. index.Where(e => e.Id != id)]);
    }

    /// <inheritdoc/>
    public async Task ExportAsync(string id, string fileName, string json)
    {
        var exportPath = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(exportPath, json);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export Masterwork save",
            File = new ShareFile(exportPath),
        });
    }

    private static async Task<List<SaveEntry>> ReadIndexAsync()
    {
        if (!File.Exists(IndexPath))
        {
            return [];
        }

        var json = await File.ReadAllTextAsync(IndexPath);
        return JsonSerializer.Deserialize<List<SaveEntry>>(json) ?? [];
    }

    private static async Task WriteIndexAsync(List<SaveEntry> entries) =>
        await File.WriteAllTextAsync(IndexPath, JsonSerializer.Serialize(entries));
}
