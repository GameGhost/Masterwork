using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class FormattedTextExpanderTests
{
    private sealed class FakeAssetResolver : IAssetResolver
    {
        public Task<string?> ResolveAsync(string assetUri) =>
            Task.FromResult(assetUri == "icon://storybook" ? "assets/icons/storybook.png" : null);
    }

    private static readonly FormattedTextExpander Expander = new(new FakeAssetResolver());

    [Fact]
    public async Task ExpandAsync_NoMarkup_HtmlEncodesPlainText()
    {
        var result = await Expander.ExpandAsync("Plain <text> & stuff");
        Assert.Equal("Plain &lt;text&gt; &amp; stuff", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_KnownIconRef_SplicesInImg()
    {
        var result = await Expander.ExpandAsync("{icon:storybook} plain text");
        Assert.Equal(
            "<img src=\"assets/icons/storybook.png\" alt=\"storybook\" class=\"mws-inline-icon\" /> plain text",
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

    [Fact]
    public async Task ExpandAsync_Bold_BecomesStrong()
    {
        var result = await Expander.ExpandAsync("**bold text**");
        Assert.Equal("<strong>bold text</strong>", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_Italic_BecomesEm()
    {
        var result = await Expander.ExpandAsync("_italic text_");
        Assert.Equal("<em>italic text</em>", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_WhitespaceInsideDelimiters_TrimmedIntoTagAndReemittedOutside()
    {
        // Malformed markdown (whitespace immediately inside the delimiters) is still handled
        // gracefully at render time — this is the tolerant half of the fix; MwsExprHelperTests
        // covers the extractor no longer generating this shape in the first place.
        var result = await Expander.ExpandAsync("**Test markdown **.");
        Assert.Equal("<strong>Test markdown</strong> .", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_IconThenLeadingWhitespaceBold_ReportedCase()
    {
        // The exact reported case: a section title built from an icon ref immediately followed by
        // a bold span whose text starts with a space.
        var result = await Expander.ExpandAsync("{icon:storybook}** Attracting Attention**");
        Assert.Equal(
            "<img src=\"assets/icons/storybook.png\" alt=\"storybook\" class=\"mws-inline-icon\" /> <strong>Attracting Attention</strong>",
            result.Value);
    }

    [Fact]
    public async Task ExpandAsync_WhitespaceOnlyBetweenDelimiters_LeftAsPlainText()
    {
        var result = await Expander.ExpandAsync("**   **");
        Assert.Equal("**   **", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_MultipleBoldSpans_EachConvertedIndependently()
    {
        var result = await Expander.ExpandAsync("**one** and **two**");
        Assert.Equal("<strong>one</strong> and <strong>two</strong>", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_HtmlInsideEmphasis_StillEncoded()
    {
        var result = await Expander.ExpandAsync("**<script>**");
        Assert.Equal("<strong>&lt;script&gt;</strong>", result.Value);
    }
}
