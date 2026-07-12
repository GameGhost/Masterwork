using System.Text;
using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class AssetResolverTests
{
    private static readonly AssetResolver Resolver = new(new GameSessionState());

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
    public async Task UnresolvedImageScheme_ReturnsNull()
    {
        // image:// has no dependency-pack/fallback tier — only bundle-local. With no module
        // loaded (empty GameSessionState), there's nothing to resolve against.
        var url = await Resolver.ResolveAsync("image://something");
        Assert.Null(url);
    }

    [Fact]
    public async Task UnsupportedScheme_ReturnsNull()
    {
        var url = await Resolver.ResolveAsync("synth://tone");
        Assert.Null(url);
    }

    [Fact]
    public async Task BundleLocalIcon_ResolvesToDataUri()
    {
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new Dictionary<string, byte[]> { ["assets/icons/village.png"] = [1, 2, 3, 4] },
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String([1, 2, 3, 4])}", url);
    }

    [Fact]
    public async Task BundleLocalImage_ResolvesToDataUri()
    {
        var bytes = Encoding.UTF8.GetBytes("<svg></svg>");
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new Dictionary<string, byte[]> { ["assets/images/MFW_Scenario_1.svg"] = bytes },
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("image://MFW_Scenario_1");

        Assert.Equal($"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocalImage_SubpathSlug_ResolvesToDataUri()
    {
        // image://setup/StorybookToken (a subpath slug) should resolve the same way as a flat
        // slug — the lookup key is built by plain concatenation ($"assets/{folder}/{slug}{ext}"),
        // so a slug containing '/' just addresses a nested asset path with no special handling.
        var bytes = Encoding.UTF8.GetBytes("fake png bytes");
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new Dictionary<string, byte[]> { ["assets/images/setup/StorybookToken.png"] = bytes },
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("image://setup/StorybookToken");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocalTakesPrecedenceOverTestPack()
    {
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new Dictionary<string, byte[]> { ["assets/icons/village.png"] = [9] },
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String([9])}", url);
    }
}
