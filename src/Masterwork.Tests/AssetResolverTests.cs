using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class AssetResolverTests
{
    private static readonly AssetResolver Resolver = new();

    [Fact]
    public async Task KnownSlug_ResolvesToTestPackUrl()
    {
        var url = await Resolver.ResolveAsync("icon://village");
        Assert.Equal("_content/Masterwork.App.Shared/assets/test-pack/village.svg", url);
    }

    [Fact]
    public async Task UnknownSlug_FallsBackToEngineIcon()
    {
        var url = await Resolver.ResolveAsync("icon://nonexistent_test_icon");
        Assert.Equal("_content/Masterwork.App.Shared/assets/fallback-icon.svg", url);
    }

    [Fact]
    public async Task NonIconScheme_ReturnsNull()
    {
        var url = await Resolver.ResolveAsync("image://something");
        Assert.Null(url);
    }
}
