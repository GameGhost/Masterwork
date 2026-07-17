using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class JsModuleStyleInjectorTests
{
    private sealed class FakeAssetResolver : IAssetResolver
    {
        public Task<string?> ResolveAsync(string assetUri) => Task.FromResult<string?>(
            assetUri switch
            {
                "image://popup/Prompt-panel" => "data:image/png;base64,AAA=",
                "icon://storybook" => "data:image/png;base64,BBB=",
                "font://averia-libre-regular" => "data:font/woff2;base64,CCC=",
                _ => null,
            });
    }

    // ResolveAssetUrlsAsync never touches IJSRuntime — null! is safe here.
    private static readonly JsModuleStyleInjector Injector = new(null!, new FakeAssetResolver());

    [Fact]
    public async Task ResolveAssetUrlsAsync_NullOrEmpty_ReturnsAsIs()
    {
        Assert.Null(await Injector.ResolveAssetUrlsAsync(null));
        Assert.Equal("", await Injector.ResolveAssetUrlsAsync(""));
    }

    [Fact]
    public async Task ResolveAssetUrlsAsync_NoAssetRefs_ReturnsUnchanged()
    {
        var css = ".mws-text { color: red; }";
        Assert.Equal(css, await Injector.ResolveAssetUrlsAsync(css));
    }

    [Fact]
    public async Task ResolveAssetUrlsAsync_UnquotedImageRef_ResolvesToDataUri()
    {
        var css = ".x { background-image: url(image://popup/Prompt-panel); }";
        var result = await Injector.ResolveAssetUrlsAsync(css);
        Assert.Equal(".x { background-image: url(\"data:image/png;base64,AAA=\"); }", result);
    }

    [Fact]
    public async Task ResolveAssetUrlsAsync_QuotedIconRef_ResolvesToDataUri()
    {
        var css = ".x { background: url('icon://storybook'); }";
        var result = await Injector.ResolveAssetUrlsAsync(css);
        Assert.Equal(".x { background: url(\"data:image/png;base64,BBB=\"); }", result);
    }

    [Fact]
    public async Task ResolveAssetUrlsAsync_UnresolvableRef_LeftUnchanged()
    {
        var css = ".x { background: url(image://popup/does-not-exist); }";
        var result = await Injector.ResolveAssetUrlsAsync(css);
        Assert.Equal(css, result);
    }

    [Fact]
    public async Task ResolveAssetUrlsAsync_FontRef_ResolvesToDataUri()
    {
        var css = "@font-face { src: url(font://averia-libre-regular) format('woff2'); }";
        var result = await Injector.ResolveAssetUrlsAsync(css);
        Assert.Equal("@font-face { src: url(\"data:font/woff2;base64,CCC=\") format('woff2'); }", result);
    }

    [Fact]
    public async Task ResolveAssetUrlsAsync_MultipleRefs_AllResolved()
    {
        var css = ".a { background: url(image://popup/Prompt-panel); } .b { background: url(icon://storybook); }";
        var result = await Injector.ResolveAssetUrlsAsync(css);
        Assert.Equal(
            ".a { background: url(\"data:image/png;base64,AAA=\"); } .b { background: url(\"data:image/png;base64,BBB=\"); }",
            result);
    }
}
