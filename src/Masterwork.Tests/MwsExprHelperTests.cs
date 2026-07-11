using Masterwork.Extractor;

namespace Masterwork.Tests;

// Covers MwsExprHelper's markdown-emphasis construction — the extractor-side half of the
// whitespace-flanking fix (see IconTextExpanderTests / FormattedTextExpanderTests for the
// render-side tolerant half). A standard markdown parser only recognizes ** or _ as emphasis when
// the delimiter isn't immediately adjacent to whitespace, so any whitespace inside a bold/italic
// run needs to end up outside the delimiters instead.
public class MwsExprHelperTests
{
    [Theory]
    [InlineData("Test markdown ", "**Test markdown** ")]
    [InlineData(" Test markdown", " **Test markdown**")]
    [InlineData(" Test markdown ", " **Test markdown** ")]
    [InlineData("Test markdown", "**Test markdown**")]
    public void WrapEmphasis_Bold_MovesFlankingWhitespaceOutsideDelimiters(string input, string expected)
    {
        Assert.Equal(expected, MwsExprHelper.WrapEmphasis(input, "**"));
    }

    [Theory]
    [InlineData("Test italic ", "_Test italic_ ")]
    [InlineData(" Test italic", " _Test italic_")]
    public void WrapEmphasis_Italic_MovesFlankingWhitespaceOutsideDelimiters(string input, string expected)
    {
        Assert.Equal(expected, MwsExprHelper.WrapEmphasis(input, "_"));
    }

    [Fact]
    public void WrapEmphasis_WhitespaceOnly_LeavesUntouched()
    {
        Assert.Equal("   ", MwsExprHelper.WrapEmphasis("   ", "**"));
    }

    [Fact]
    public void BuildValueFromRuns_TrailingSpaceInBoldRun_MovesOutsideClosingDelimiter()
    {
        // The exact reported case: a section title built from an icon run followed by a bold run
        // whose text has a leading space ("{icon:storybook}" + " Attracting Attention") used to
        // produce "{icon:storybook}** Attracting Attention**" — malformed on both ends.
        var value = MwsExprHelper.BuildValueFromRuns(
        [
            new TextRun { AssetRef = "icon://storybook" },
            new TextRun { Text = " Attracting Attention", Style = "bold" },
        ]);

        Assert.Equal("{icon:storybook} **Attracting Attention**", value);
    }

    [Fact]
    public void BuildValueFromRuns_IconThenSpaceRunThenLeadingSpaceBoldRun_CollapsesDoubleSpace()
    {
        // A separate real bug found while re-extracting: when there's already a standalone
        // plain-text space run between the icon and the bold run (rather than the space living
        // inside the bold run itself, as in the case above), WrapEmphasis correctly moves the bold
        // run's own leading space outside its delimiters — but that lands it right next to the
        // pre-existing space run, producing "{icon:storybook}  **Suspicion**" (two spaces) unless
        // the two are collapsed back into one.
        var value = MwsExprHelper.BuildValueFromRuns(
        [
            new TextRun { AssetRef = "icon://storybook" },
            new TextRun { Text = " ", Style = null },
            new TextRun { Text = " Suspicion", Style = "bold" },
        ]);

        Assert.Equal("{icon:storybook} **Suspicion**", value);
    }

    [Fact]
    public void BuildValueFromRuns_MultiRunBoldSpan_WrapsOnceAroundCombinedText()
    {
        // Two consecutive bold runs (e.g. split by the visitor for unrelated reasons) must merge
        // into a single **...** span, not "**Run1****Run2**".
        var value = MwsExprHelper.BuildValueFromRuns(
        [
            new TextRun { Text = "Turn to ", Style = null },
            new TextRun { Text = "The Cost of Disease", Style = "bold" },
            new TextRun { Text = " book.", Style = null },
        ]);

        Assert.Equal("Turn to **The Cost of Disease** book.", value);
    }

    [Fact]
    public void BuildValueFromRuns_BoldRunFollowedDirectlyByPlainText_NoTrailingSpaceLeaksInside()
    {
        var value = MwsExprHelper.BuildValueFromRuns(
        [
            new TextRun { Text = "Heart ", Style = "bold" },
            new TextRun { Text = "token", Style = null },
        ]);

        Assert.Equal("**Heart** token", value);
    }
}
