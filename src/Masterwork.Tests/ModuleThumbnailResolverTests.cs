using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class ModuleThumbnailResolverTests
{
    // Same minimal in-memory IModuleAssetSource shape as AssetResolverTests' own adapter.
    private sealed class DictionaryModuleAssetSource(IReadOnlyDictionary<string, byte[]> assets) : IModuleAssetSource
    {
        public Task<byte[]?> GetAssetAsync(string assetPath) =>
            Task.FromResult(assets.TryGetValue(assetPath, out var bytes) ? bytes : null);

        public Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) =>
            Task.FromResult(assets.TryGetValue(assetPath, out var bytes)
                ? $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}"
                : null);

        public Task<IReadOnlyList<string>> ListAssetPathsAsync() =>
            Task.FromResult<IReadOnlyList<string>>([.. assets.Keys]);
    }

    [Fact]
    public async Task NullImageUri_ReturnsNull()
    {
        var assets = new DictionaryModuleAssetSource(new Dictionary<string, byte[]>());
        var url = await ModuleThumbnailResolver.ResolveAsync(assets, null);
        Assert.Null(url);
    }

    [Fact]
    public async Task NonImageScheme_ReturnsNull()
    {
        var assets = new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/images/scenario_tile_tcod.png"] = [1, 2, 3] });
        var url = await ModuleThumbnailResolver.ResolveAsync(assets, "icon://scenario_tile_tcod");
        Assert.Null(url);
    }

    [Fact]
    public async Task NoMatchingAsset_ReturnsNull()
    {
        var assets = new DictionaryModuleAssetSource(new Dictionary<string, byte[]>());
        var url = await ModuleThumbnailResolver.ResolveAsync(assets, "image://scenario_tile_tcod");
        Assert.Null(url);
    }

    [Fact]
    public async Task PngSlug_ResolvesToDataUri()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var assets = new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/images/scenario_tile_tcod.png"] = bytes });

        var url = await ModuleThumbnailResolver.ResolveAsync(assets, "image://scenario_tile_tcod");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task SvgSlug_TriesExtensionsInOrder_ResolvesToDataUri()
    {
        // .png isn't present, so the second extension tried (.svg) should win.
        var bytes = new byte[] { 5, 6, 7 };
        var assets = new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/images/tile.svg"] = bytes });

        var url = await ModuleThumbnailResolver.ResolveAsync(assets, "image://tile");

        Assert.Equal($"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}", url);
    }
}
