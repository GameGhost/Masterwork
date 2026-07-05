using Masterwork.App.Shared.Services;

namespace Masterwork.App.Services;

/// <inheritdoc cref="ISaveStore"/>
/// <remarks>Backed by <see cref="FileSystem"/>'s app data directory and the MAUI <see cref="Share"/> API for export. MAUI-only — lives in the MAUI head rather than the platform-agnostic Shared project.</remarks>
public sealed class FileSaveStore : ISaveStore
{
    private static string PathFor(int slot) => Path.Combine(FileSystem.AppDataDirectory, $"save-{slot}.json");

    /// <inheritdoc/>
    public Task SaveAsync(int slot, string json) => File.WriteAllTextAsync(PathFor(slot), json);

    /// <inheritdoc/>
    public async Task<string?> LoadAsync(int slot)
    {
        var path = PathFor(slot);
        return File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
    }

    /// <inheritdoc/>
    public Task<bool> HasSaveAsync(int slot) => Task.FromResult(File.Exists(PathFor(slot)));

    /// <inheritdoc/>
    public async Task ExportAsync(int slot, string fileName, string json)
    {
        var exportPath = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllTextAsync(exportPath, json);
        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Export Masterwork save",
            File = new ShareFile(exportPath),
        });
    }
}
