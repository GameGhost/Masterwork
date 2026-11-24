using Masterwork.Translation;

namespace Masterwork.Tests;

public sealed class TranslationComparisonTests
{
    private const TranslationFileKind Restext = TranslationFileKind.Restext;

    private static readonly TranslationLocale FrCa = TranslationLocale.For("fr-CA");

    [Fact]
    public void Prose_DropsPlaceholdersAndResxTags()
    {
        Assert.Equal("Gain  now", TranslationComparison.Prose("Gain {icon:coin_token} now", Restext));
        Assert.Equal("For you", TranslationComparison.Prose("For <em>you</em>", TranslationFileKind.Resx));
    }

    [Fact]
    public void LengthRatio_IsMeasuredOnProse()
    {
        // Prose is " Move the marker left." (22) and " Déplacez-le." (13): 0.59. Counting the placeholder would give 0.78, in range.
        const string source = "{icon:angrymob_icon} Move the marker left.";
        const string target = "{icon:angrymob_icon} Déplacez-le.";

        var ratio = TranslationComparison.LengthRatioOutOfRange(source, target, Restext, FrCa);

        Assert.NotNull(ratio);
        Assert.Equal(13.0 / 22.0, ratio!.Value, 3);
    }

    [Fact]
    public void LengthRatio_SkipsSourceProseOfTwentyCharactersOrLess()
    {
        Assert.Null(TranslationComparison.LengthRatioOutOfRange("{n}Twenty chars exactly", "{n}Court.", Restext, FrCa));
    }

    [Theory]
    [InlineData("Ask {nameC} now", "Demandez à {nameC}maintenant", "Demandez à {nameC} maintenant")]
    [InlineData("Gain **15** coins", "Gagnez **15**pièces", "Gagnez **15** pièces")]
    [InlineData("Read _this_ now", "Lisez _ceci_maintenant", "Lisez _ceci_ maintenant")]
    [InlineData("{a} and {b} go", "{a}et {b}partent", "{a} et {b} partent")]
    public void RestoreSpaces_PutsBackASpaceTheSourceHas(string source, string target, string expected)
    {
        Assert.Equal(expected, TranslationComparison.RestoreSpacesAfterClosers(target, source, Restext));
    }

    [Theory]
    [InlineData("The {n}th round", "La {n}e manche")]
    [InlineData("Ask {nameC}", "Demandez à {nameC}.")]
    public void RestoreSpaces_LeavesGluingTheSourceAlsoHas(string source, string target)
    {
        Assert.Equal(target, TranslationComparison.RestoreSpacesAfterClosers(target, source, Restext));
    }

    [Fact]
    public void RestoreSpaces_PairsRepeatedPlaceholdersInOrder()
    {
        const string source = "{n}x then {n} more";

        Assert.Equal("{n}x puis {n} de plus", TranslationComparison.RestoreSpacesAfterClosers("{n}x puis {n}de plus", source, Restext));
    }

    [Fact]
    public void RestoreSpaces_WithDifferentCounts_OnlyWhenTheSourceSpacesEveryUse()
    {
        Assert.Equal("{n} a, {n} b, {n} c", TranslationComparison.RestoreSpacesAfterClosers("{n}a, {n}b, {n}c", "{n} a and {n} b", Restext));
        Assert.Equal("{n}a, {n}b, {n}c", TranslationComparison.RestoreSpacesAfterClosers("{n}a, {n}b, {n}c", "{n}x and {n} b", Restext));
    }
}
