using Masterwork.ModulePacker.Translation;

namespace Masterwork.Tests;

public sealed class FrenchTypographyTests
{
    private const char Nbsp = ' ';

    private static TypographyResult Restext(string value) => FrenchTypography.Normalize(value, TranslationFileKind.Restext);

    [Theory]
    [InlineData("Prêt ?", "Prêt?")]
    [InlineData("Allez !", "Allez!")]
    [InlineData("un ; deux", "un; deux")]
    [InlineData("Vraiment ?", "Vraiment?")]
    [InlineData("Vraiment ?!", "Vraiment?!")]
    public void SpaceBeforeHighPunctuation_IsRemoved(string input, string expected)
    {
        var result = Restext(input);

        Assert.Equal(expected, result.Value);
        Assert.Equal([FrenchTypography.NoSpaceBeforeHighPunctuation], result.Rules);
    }

    [Theory]
    [InlineData("Note: lire", "Note : lire")]
    [InlineData("Note : lire", "Note : lire")]
    [InlineData("Note : lire", "Note : lire")]
    [InlineData("**Note**: lire", "**Note** : lire")]
    [InlineData("Total {n}: fin", "Total {n} : fin")]
    public void ColonGetsOneNonBreakingSpaceBeforeIt(string input, string expected)
    {
        var result = Restext(input);

        Assert.Equal(expected, result.Value);
        Assert.Equal([FrenchTypography.NonBreakingSpaceBeforeColon], result.Rules);
    }

    [Theory]
    [InlineData("Voir https://masterworkapp.com")]
    [InlineData("À 12:30 précises")]
    [InlineData("Coût {icon:coin_token} et {a:b}")]
    [InlineData(":début")]
    [InlineData("Note : déjà correct")]
    public void ColonsFrenchDoesNotSpace_AreLeftAlone(string input)
    {
        var result = Restext(input);

        Assert.Equal(input, result.Value);
        Assert.False(result.Changed);
    }

    [Theory]
    [InlineData("Cliquez «RÉVÉLER»", "Cliquez « RÉVÉLER »")]
    [InlineData("Cliquez « RÉVÉLER »", "Cliquez « RÉVÉLER »")]
    [InlineData("Cliquez «  RÉVÉLER »", "Cliquez « RÉVÉLER »")]
    public void Guillemets_GetNonBreakingSpacesInside(string input, string expected)
    {
        var result = Restext(input);

        Assert.Equal(expected, result.Value);
        Assert.Equal([FrenchTypography.NonBreakingSpaceInsideGuillemets], result.Rules);
    }

    [Fact]
    public void SeveralRules_AreAllReported()
    {
        var result = Restext("Prêt ? Choix: «oui»");

        Assert.Equal($"Prêt? Choix{Nbsp}: «{Nbsp}oui{Nbsp}»", result.Value);
        Assert.Equal(
            [FrenchTypography.NoSpaceBeforeHighPunctuation, FrenchTypography.NonBreakingSpaceBeforeColon, FrenchTypography.NonBreakingSpaceInsideGuillemets],
            result.Rules);
    }

    [Fact]
    public void Resx_LeavesHtmlTagsAlone()
    {
        const string input = "Voir <a href=\"https://x.org/a:b\" title=\"a ?\">ici</a>.";

        var result = FrenchTypography.Normalize(input, TranslationFileKind.Resx);

        Assert.Equal(input, result.Value);
    }

    [Fact]
    public void OnlySpacingChanges()
    {
        const string input = "Le **jeton** {name} : « vous » ? Oui !";

        var result = Restext(input);

        static string Strip(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        Assert.Equal(Strip(input), Strip(result.Value));
    }
}
