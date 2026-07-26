using Masterwork.App.Shared.Services;

namespace Masterwork.App.Services;

/// <inheritdoc cref="IModuleAssetSource"/>
/// <remarks>
/// Fetches one asset at a time from a module's extracted directory on disk — a plain
/// <see cref="File.ReadAllBytesAsync(string, CancellationToken)"/> per call, so a passage that
/// references only a handful of images never pays for reading the module's other assets. Builds a
/// <c>data:</c> URI from the bytes, entirely in .NET memory — no JS interop marshaling cost applies
/// here the way it does for <c>IndexedDbModuleAssetSource</c>'s IndexedDB-backed reads, since the
/// bytes never need to cross a JS/.NET boundary to reach a local file. Constructed fresh by
/// <see cref="FileModuleStore.LoadAsync"/> for each loaded module; holds only the module's own
/// extracted directory path.
/// </remarks>
public sealed class FileModuleAssetSource(string moduleDirectory) : IModuleAssetSource
{
    /// <inheritdoc/>
    public async Task<byte[]?> GetAssetAsync(string assetPath)
    {
        var path = Path.Combine(moduleDirectory, assetPath);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path) : null;
    }

    /// <inheritdoc/>
    public async Task<string?> GetAssetUrlAsync(string assetPath, string mimeType)
    {
        var bytes = await GetAssetAsync(assetPath);
        return bytes is null ? null : $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListAssetPathsAsync()
    {
        var assetsDir = Path.Combine(moduleDirectory, "assets");
        if (!Directory.Exists(assetsDir))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        IReadOnlyList<string> paths = Directory.EnumerateFiles(assetsDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(moduleDirectory, f).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();
        return Task.FromResult(paths);
    }
}
