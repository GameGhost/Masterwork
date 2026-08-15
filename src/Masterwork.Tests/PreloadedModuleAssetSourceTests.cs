using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class PreloadedModuleAssetSourceTests
{
    // Counts calls so tests can confirm preloading actually resolves every listed asset up front, and
    // that GetAssetUrlAsync afterward is served from cache rather than re-invoking the inner source.
    // Urls are keyed by path only (mimeType is passed through untouched — these tests don't need to
    // vary it) since PreloadedModuleAssetSource derives its own mimeType per path via AssetMimeTypes.
    private sealed class CountingAssetSource(IReadOnlyDictionary<string, string> urls) : IModuleAssetSource
    {
        public int GetAssetUrlCallCount { get; private set; }

        public Task<byte[]?> GetAssetAsync(string assetPath) => Task.FromResult<byte[]?>(null);

        public Task<string?> GetAssetUrlAsync(string assetPath, string mimeType)
        {
            GetAssetUrlCallCount++;
            return Task.FromResult(urls.TryGetValue(assetPath, out var url) ? url : null);
        }

        public Task<IReadOnlyList<string>> ListAssetPathsAsync() =>
            Task.FromResult<IReadOnlyList<string>>([.. urls.Keys]);
    }

    // Progress<T> posts each Report call through whatever SynchronizationContext was current at
    // construction time. xUnit test threads have none installed by default, so Report defers to the
    // thread pool instead of running inline — meaning PreloadAllAsync can return, and the awaiting
    // test can reach its assertions, before every queued report has actually been delivered. This is
    // a real (if low-probability) race in the two progress-reporting tests below, not a SUT bug: the
    // real app always has a synchronization context (Blazor's own renderer context), which is what
    // makes Report() run synchronously there. Installing an equivalent synchronous context for the
    // duration of these two tests makes them deterministic without changing PreloadedModuleAssetSource
    // itself.
    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    private static async Task<List<(int Done, int Total)>> PreloadWithSynchronousProgressAsync(PreloadedModuleAssetSource preloaded)
    {
        var reports = new List<(int Done, int Total)>();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        try
        {
            var progress = new Progress<(int Done, int Total)>(reports.Add);
            await preloaded.PreloadAllAsync(progress);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        return reports;
    }

    [Fact]
    public async Task PreloadAllAsync_ResolvesEveryListedAsset()
    {
        var inner = new CountingAssetSource(new Dictionary<string, string>
        {
            ["assets/icons/a.png"] = "blob:a",
            ["assets/icons/b.png"] = "blob:b",
            ["assets/images/c.png"] = "blob:c",
        });
        var preloaded = new PreloadedModuleAssetSource(inner);

        await preloaded.PreloadAllAsync();

        Assert.Equal(3, inner.GetAssetUrlCallCount);
        Assert.Equal("blob:a", await preloaded.GetAssetUrlAsync("assets/icons/a.png", "image/png"));
        Assert.Equal("blob:b", await preloaded.GetAssetUrlAsync("assets/icons/b.png", "image/png"));
        Assert.Equal("blob:c", await preloaded.GetAssetUrlAsync("assets/images/c.png", "image/png"));
    }

    [Fact]
    public async Task GetAssetUrlAsync_AfterPreload_ServedFromCacheWithoutCallingInnerAgain()
    {
        var inner = new CountingAssetSource(new Dictionary<string, string> { ["assets/icons/a.png"] = "blob:a" });
        var preloaded = new PreloadedModuleAssetSource(inner);
        await preloaded.PreloadAllAsync();
        Assert.Equal(1, inner.GetAssetUrlCallCount);

        await preloaded.GetAssetUrlAsync("assets/icons/a.png", "image/png");
        await preloaded.GetAssetUrlAsync("assets/icons/a.png", "image/png");

        Assert.Equal(1, inner.GetAssetUrlCallCount);
    }

    [Fact]
    public async Task GetAssetUrlAsync_PathNotPreloaded_FallsBackToInnerBeforePreloadRuns()
    {
        // Before PreloadAllAsync has ever run, a miss falls back to a live lookup rather than
        // assuming the path doesn't exist — only once preloading has completed is a cache miss
        // treated as authoritative (see the next test).
        var inner = new CountingAssetSource(new Dictionary<string, string> { ["assets/icons/a.png"] = "blob:a" });
        var preloaded = new PreloadedModuleAssetSource(inner);

        var url = await preloaded.GetAssetUrlAsync("assets/icons/a.png", "image/png");

        Assert.Equal("blob:a", url);
        Assert.Equal(1, inner.GetAssetUrlCallCount);
    }

    [Fact]
    public async Task GetAssetUrlAsync_AfterPreload_MissingPathReturnsNullWithoutLiveLookup()
    {
        // Once a full preload has completed, ListAssetPathsAsync's result is authoritative — a path
        // that was never listed is definitively absent, so no live round-trip should be spent
        // confirming it (relevant since AssetResolver tries several candidate extensions per slug).
        var inner = new CountingAssetSource(new Dictionary<string, string> { ["assets/icons/a.png"] = "blob:a" });
        var preloaded = new PreloadedModuleAssetSource(inner);
        await preloaded.PreloadAllAsync();
        Assert.Equal(1, inner.GetAssetUrlCallCount);

        var url = await preloaded.GetAssetUrlAsync("assets/icons/nonexistent.png", "image/png");

        Assert.Null(url);
        Assert.Equal(1, inner.GetAssetUrlCallCount);
    }

    [Fact]
    public async Task PreloadAllAsync_ReportsProgressFromZeroToTotal()
    {
        var inner = new CountingAssetSource(new Dictionary<string, string>
        {
            ["a"] = "blob:a",
            ["b"] = "blob:b",
            ["c"] = "blob:c",
            ["d"] = "blob:d",
        });
        var preloaded = new PreloadedModuleAssetSource(inner);

        var reports = await PreloadWithSynchronousProgressAsync(preloaded);

        Assert.Contains((0, 4), reports);
        Assert.Contains((4, 4), reports);
    }

    [Fact]
    public async Task PreloadAllAsync_NoAssets_ReportsZeroTotalAndCompletes()
    {
        var inner = new CountingAssetSource(new Dictionary<string, string>());
        var preloaded = new PreloadedModuleAssetSource(inner);

        var reports = await PreloadWithSynchronousProgressAsync(preloaded);

        Assert.Equal([(0, 0)], reports);
    }

    [Fact]
    public async Task GetAssetAsync_DelegatesStraightToInner()
    {
        // GetAssetAsync (raw bytes, for style.css) is never cached here — it's called once per
        // module load, well before preloading runs, so there's nothing to gain by caching it.
        var inner = new CountingAssetSource(new Dictionary<string, string>());
        var preloaded = new PreloadedModuleAssetSource(inner);

        var bytes = await preloaded.GetAssetAsync("assets/style.css");

        Assert.Null(bytes);
    }
}
