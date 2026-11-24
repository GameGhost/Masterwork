using Masterwork.Translation;

namespace Masterwork.Tests;

public sealed class FrenchTypographyTests
{
    private const char Nbsp = '\u00A0';

    private static TypographyResult Restext(string value) => FrenchTypography.Normalize(value, TranslationFileKind.Restext);

    [Theory]
    [InlineData("Prêt ?", "Prêt?")]
    [InlineData("Allez !", "Allez!")]
    [InlineData("un ; deux", "un; deux")]
    [InlineData("Vraiment\u00A0?", "Vraiment?")]
    [InlineData("Vraiment\u202F?!", "Vraiment?!")]
    public void SpaceBeforeHighPunctuation_IsRemoved(string input, string expected)
    {
        var result = Restext(input);

        Assert.Equal(expected, result.Value);
        Assert.Equal([FrenchTypography.NoSpaceBeforeHighPunctuation], result.Rules);
    }

    [Theory]
    [InlineData("Note: lire", "Note\u00A0: lire")]
    [InlineData("Note : lire", "Note\u00A0: lire")]
    [InlineData("Note\u202F: lire", "Note\u00A0: lire")]
    [InlineData("**Note**: lire", "**Note**\u00A0: lire")]
    [InlineData("Total {n}: fin", "Total {n}\u00A0: fin")]
    [InlineData("//Apart\u00E9//: fin", "//Apart\u00E9//\u00A0: fin")]
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
    [InlineData("Score {0}:{1}")]
    [InlineData("À {h}:30 ou 12:{m}")]
    [InlineData("Note\u00A0: déjà correct")]
    public void ColonsFrenchDoesNotSpace_AreLeftAlone(string input)
    {
        var result = Restext(input);

        Assert.Equal(input, result.Value);
        Assert.False(result.Changed);
    }

    [Theory]
    [InlineData("Cliquez «RÉVÉLER»", "Cliquez «\u00A0RÉVÉLER\u00A0»")]
    [InlineData("Cliquez « RÉVÉLER »", "Cliquez «\u00A0RÉVÉLER\u00A0»")]
    [InlineData("Cliquez «  RÉVÉLER\u202F»", "Cliquez «\u00A0RÉVÉLER\u00A0»")]
    public void Guillemets_GetNonBreakingSpacesInside(string input, string expected)
    {
        var result = Restext(input);

        Assert.Equal(expected, result.Value);
        Assert.Equal([FrenchTypography.NonBreakingSpaceInsideGuillemets], result.Rules);
    }

    [Theory]
    [InlineData("«»")]
    [InlineData("« ?")]
    [InlineData("«?»")]
    [InlineData("«  » !")]
    [InlineData("Dit : « Prêt ? » ; puis «non» !")]
    public void NormalizedOutput_IsAlreadyNormal(string input)
    {
        var once = Restext(input).Value;

        var twice = Restext(once);

        Assert.False(twice.Changed, $"'{once}' changed again to '{twice.Value}'");
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
