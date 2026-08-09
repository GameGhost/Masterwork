using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class FormattedTextExpanderTests
{
    private sealed class FakeAssetResolver : IAssetResolver
    {
        public Task<string?> ResolveAsync(string assetUri) => Task.FromResult(assetUri switch
        {
            "icon://storybook" => "assets/icons/storybook.png",
            "icon://creepy_icon" => "assets/icons/creepy_icon.png",
            "icon://insanity_icon" => "assets/icons/insanity_icon.png",
            _ => null,
        });
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

    [Fact]
    public async Task ExpandAsync_ItalicSpanWrappingIconRefs_ReportedCase()
    {
        // Reported case (restext _35VP_008): an italic span whose opening/closing _ sit on
        // opposite sides of one or more {icon:...} refs. The old icon-first implementation split
        // the string into icon-separated segments and matched emphasis independently within each,
        // so the opening _ and closing _ landed in different segments and neither ever found its
        // pair - both rendered as literal underscores. Icon slugs containing underscores
        // themselves (creepy_icon, insanity_icon) is also the reason a naive "match emphasis on
        // the raw string first" fix would be wrong: the regex would treat the underscore inside
        // the slug as a delimiter.
        var result = await Expander.ExpandAsync(
            " _(They gain 2 {icon:creepy_icon} and 1 {icon:insanity_icon}.)_");

        Assert.Equal(
            " <em>(They gain 2 <img src=\"assets/icons/creepy_icon.png\" alt=\"creepy_icon\" class=\"mws-inline-icon\" /> and 1 " +
            "<img src=\"assets/icons/insanity_icon.png\" alt=\"insanity_icon\" class=\"mws-inline-icon\" />.)</em>",
            result.Value);
    }

    [Fact]
    public async Task ExpandAsync_BoldSpanWrappingIconRef_StillWorks()
    {
        var result = await Expander.ExpandAsync("**Gain 1 {icon:creepy_icon} token**");
        Assert.Equal(
            "<strong>Gain 1 <img src=\"assets/icons/creepy_icon.png\" alt=\"creepy_icon\" class=\"mws-inline-icon\" /> token</strong>",
            result.Value);
    }

    [Fact]
    public async Task ExpandAsync_BoldNestedInsideItalic_ReportedCase()
    {
        // Reported case (A Time of War's Reign3_005): "_...Choose one **animal token** and return
        // it...player._". The only other underscore in the whole string is the italic span's own
        // closing one, so the non-greedy italic match's [\s\S]*? has no choice but to span the
        // entire sentence — "**animal token**" ends up entirely inside that one match's own
        // captured group, never independently visited as its own top-level Regex.Matches result.
        // Before the recursive fix, the inner content was appended via plain icon-expansion only,
        // so "**animal token**" rendered as literal, un-bolded text inside the <em>.
        var result = await Expander.ExpandAsync("_Choose one **animal token** and return it._");
        Assert.Equal("<em>Choose one <strong>animal token</strong> and return it.</em>", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_ItalicNestedInsideBold_AlsoWorks()
    {
        // Contrast direction: the outer span is bold this time, with an italic span entirely
        // inside its own captured group — same recursive fix, opposite nesting order.
        var result = await Expander.ExpandAsync("**Gain 1 _rare_ token**");
        Assert.Equal("<strong>Gain 1 <em>rare</em> token</strong>", result.Value);
    }

    [Fact]
    public async Task ExpandAsync_NestedEmphasisWithIconRefInsideInnerSpan_AllThreeCombine()
    {
        // The recursive pass and the icon-masking pass must compose correctly: an icon ref sits
        // inside the INNER (bold) span, which itself sits inside the outer (italic) span.
        var result = await Expander.ExpandAsync("_gain **1 {icon:creepy_icon} token** total_");
        Assert.Equal(
            "<em>gain <strong>1 <img src=\"assets/icons/creepy_icon.png\" alt=\"creepy_icon\" class=\"mws-inline-icon\" /> token</strong> total</em>",
            result.Value);
    }
}
