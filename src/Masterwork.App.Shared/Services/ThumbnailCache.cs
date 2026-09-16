using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Keeps browse thumbnails across catalog refreshes, keyed by content hash.
///
/// Thumbnails are by far the largest thing a browse view fetches — the real ones are a few hundred
/// KB each — and their paths don't change when the art does, so a path alone can't say whether a
/// cached copy is still current. <see cref="CatalogEntry.ThumbnailSha256"/> can: a cached entry is
/// reusable exactly when its hash matches what the catalog now says, which means a refresh that
/// changes nothing costs no image downloads at all.
/// </summary>
public sealed class ThumbnailCache(
    IContentDownloader downloader,
    IJSRuntime js,
    ILogger<ThumbnailCache> logger)
{
    private const string KeyPrefix = "masterwork.thumb.";

    // Kept per session as well as in storage: a browse list re-rendering shouldn't re-read and
    // re-decode the same base64 out of localStorage for every tile.
    private readonly Dictionary<string, string> _inMemory = [];

    /// <summary>
    /// Returns a data URL for the entry's thumbnail, downloading it only if nothing cached matches
    /// <see cref="CatalogEntry.ThumbnailSha256"/>. Returns <see langword="null"/> for an entry with
    /// no thumbnail, or when the download fails — a missing tile image is not worth failing a browse
    /// list over.
    /// </summary>
    public async Task<string?> GetAsync(ContentSource source, CatalogEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry.Thumbnail is not { } thumbnail)
        {
            return null;
        }

        // Keyed by hash, not by id: a changed thumbnail is a different key, so a stale copy is never
        // served and the old one simply falls out of use.
        var key = KeyPrefix + thumbnail.Hash;

        if (_inMemory.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (await ReadAsync(key) is { } stored)
        {
            _inMemory[key] = stored;
            return stored;
        }

        try
        {
            var bytes = await downloader.GetAsync(source.ResolveThumbnailUrl(thumbnail.Path), progress: null, cancellationToken);
            var dataUrl = $"data:{AssetMimeTypes.ResolveMimeType(thumbnail.Path)};base64,{Convert.ToBase64String(bytes)}";

            _inMemory[key] = dataUrl;
            await WriteAsync(key, dataUrl);
            return dataUrl;
        }
        catch (Exception ex) when (ex is ContentDownloadException or ArgumentException)
        {
            logger.LogWarning(ex, "Could not fetch thumbnail {Path} for {Id}", thumbnail.Path, entry.Id);
            return null;
        }
    }

    private async Task<string?> ReadAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private async Task WriteAsync(string key, string dataUrl)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, dataUrl);
        }
        catch (JSException ex)
        {
            // Storage quota is the likely cause, and thumbnails are the first thing worth giving up
            // on when it's tight — the session-local copy still serves this run.
            logger.LogWarning(ex, "Could not cache thumbnail under {Key}", key);
        }
    }
}
