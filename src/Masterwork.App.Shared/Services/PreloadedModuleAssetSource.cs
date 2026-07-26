using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IModuleAssetSource"/>
/// <remarks>
/// Wraps another <see cref="IModuleAssetSource"/> and resolves every asset it lists up front
/// (<see cref="PreloadAllAsync"/>), caching the resulting URLs so <see cref="GetAssetUrlAsync"/> is
/// instant afterward — used by the Play/Resume flow in <c>StartNewGame.razor</c> so a passage's
/// images/fonts are already resident by the time it first renders, instead of popping in one at a
/// time as lazy fetches complete. Resolutions run in small concurrent batches rather than one at a
/// time — even on single-threaded WASM, multiple in-flight IndexedDB reads let the browser's own I/O
/// overlap instead of serializing strictly one full round-trip after another. Caches URLs, not raw
/// bytes: for an <see cref="IndexedDbModuleAssetSource"/>-backed <paramref name="inner"/>, the actual
/// asset bytes never enter .NET memory at all (see that type's remarks) — only the short URL string
/// does, which is what keeps this preload step's memory footprint proportionate to the module's
/// asset *count*, not its total asset *size*.
/// </remarks>
public sealed class PreloadedModuleAssetSource(IModuleAssetSource inner) : IModuleAssetSource
{
    private const int Concurrency = 6;

    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private bool _preloaded;

    /// <summary>Resolves every asset <paramref name="inner"/> lists, reporting (done, total) as each completes.</summary>
    public async Task PreloadAllAsync(IProgress<(int Done, int Total)>? progress = null)
    {
        var paths = await inner.ListAssetPathsAsync();
        var total = paths.Count;
        progress?.Report((0, total));
        if (total == 0)
        {
            _preloaded = true;
            return;
        }

        var done = 0;
        foreach (var batch in paths.Chunk(Concurrency))
        {
            await Task.WhenAll(batch.Select(async path =>
            {
                var mimeType = AssetMimeTypes.ResolveMimeType(path);
                var url = await inner.GetAssetUrlAsync(path, mimeType);
                _cache[path] = url;
                Interlocked.Increment(ref done);
                progress?.Report((done, total));
            }));
        }

        _preloaded = true;
    }

    /// <inheritdoc/>
    /// <remarks>Not cached — used once per module load (style.css), well before preloading happens, so there's nothing to gain by caching it here.</remarks>
    public Task<byte[]?> GetAssetAsync(string assetPath) => inner.GetAssetAsync(assetPath);

    /// <inheritdoc/>
    public async Task<string?> GetAssetUrlAsync(string assetPath, string mimeType)
    {
        if (_cache.TryGetValue(assetPath, out var cached))
        {
            return cached;
        }

        // Once a full preload has completed, ListAssetPathsAsync's result is authoritative — a miss
        // means the path definitively doesn't exist, so there's no need to pay for a live round-trip
        // to confirm it (relevant since AssetResolver tries several candidate extensions per slug,
        // only one of which usually exists).
        return _preloaded ? null : await inner.GetAssetUrlAsync(assetPath, mimeType);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ListAssetPathsAsync() => inner.ListAssetPathsAsync();

    /// <summary>
    /// Revokes every <c>blob:</c> URL this preload created, via <c>wwwroot/moduleStore.js</c>'s
    /// <c>revokeObjectUrl</c> — call once this module unloads (a new module is about to load, or the
    /// session is abandoned), or the browser keeps each blob's backing data alive for the rest of the
    /// tab's lifetime. <c>data:</c> URIs (MAUI's <see cref="FileModuleAssetSource"/>) and the empty
    /// source's nulls are skipped — nothing to revoke there.
    /// </summary>
    public async Task RevokeAllAsync(IJSRuntime js)
    {
        var urls = _cache.Values.Where(url => url is { } u && u.StartsWith("blob:", StringComparison.Ordinal)).ToList();
        if (urls.Count == 0)
        {
            return;
        }

        var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Masterwork.App.Shared/moduleStore.js");
        foreach (var url in urls)
        {
            await module.InvokeVoidAsync("revokeObjectUrl", url);
        }
    }
}
