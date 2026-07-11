using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class IconTextExpanderTests
{
    private sealed class FakeAssetResolver : IAssetResolver
    {
        public Task<string?> ResolveAsync(string assetUri) =>
            Task.FromResult(assetUri == "icon://storybook" ? "assets/icons/storybook.png" : null);
    }

    private static readonly IconTextExpander Expander = new(new FakeAssetResolver());

    [Fact]
    public async Task ExpandAsync_NoIconRefs_HtmlEncodesPlainText()
    {
        var result = await Expander.ExpandAsync("Plain <text> & stuff");
        Assert.Equal("Plain &lt;text&gt; &amp; stuff", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_KnownIconRef_SplicesInImg()
    {
        var result = await Expander.ExpandAsync("{icon:storybook}** Attracting Attention**");
        Assert.Equal(
            "<img src=\"assets/icons/storybook.png\" alt=\"storybook\" class=\"mws-inline-icon\" />** Attracting Attention**",
            result.Value);
    }

    [Fact]
    public async Task ExpandAsync_UnknownIconRef_LeavesLiteralTextEncoded()
    {
        var result = await Expander.ExpandAsync("{icon:nonexistent_test_icon}");
        Assert.Equal("{icon:nonexistent_test_icon}", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_NullOrEmpty_ReturnsEmptyMarkup()
    {
        Assert.Equal(string.Empty, (await Expander.ExpandAsync(null)).Value);
        Assert.Equal(string.Empty, (await Expander.ExpandAsync("")).Value);
    }
}
