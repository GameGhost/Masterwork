using Masterwork.ModulePacker.Translation;

namespace Masterwork.Tests;

public sealed class TranslationChecksTests
{
    private static StringVerdict Restext(string en, string fr, TermBase? terms = null) =>
        TranslationChecks.Check(en, fr, TranslationFileKind.Restext, terms);

    private static StringVerdict Resx(string en, string fr) =>
        TranslationChecks.Check(en, fr, TranslationFileKind.Resx);

    private static IEnumerable<string> FailureIds(StringVerdict v) => v.Failures.Select(f => f.Check);

    private static IEnumerable<string> FlagIds(StringVerdict v) => v.Flags.Select(f => f.Check);

    [Fact]
    public void FaithfulTranslation_IsClean()
    {
        var verdict = Restext(
            "Gain **3VP** and place {count} tokens on _the Journal_.",
            "Gagnez **3 PV** et placez {count} jetons sur _le Journal_.");

        Assert.Empty(verdict.Failures);
        Assert.Empty(verdict.Flags);
    }

    [Fact]
    public void EmptyFrench_ForNonEmptyEnglish_Fails()
    {
        Assert.Equal([TranslationChecks.Empty], FailureIds(Restext("Continue", "")));
        Assert.Empty(Restext("", "").Failures);
    }

    [Theory]
    [InlineData("You have {n} coins.", "Vous avez des pièces.")]
    [InlineData("You have {n} coins.", "Vous avez {count} pièces.")]
    [InlineData("{a} and {a}", "{a} et")]
    public void PlaceholderMultisetMismatch_Fails(string en, string fr)
    {
        Assert.Contains(TranslationChecks.Placeholders, FailureIds(Restext(en, fr)));
    }

    [Fact]
    public void ReorderedPlaceholders_Pass()
    {
        Assert.DoesNotContain(TranslationChecks.Placeholders, FailureIds(Restext("{who} gives {what}.", "{what} est donné par {who}.")));
    }

    [Theory]
    [InlineData("Gain **3VP**.", "Gagnez **3 PV.")]
    [InlineData("_Quietly_ leave.", "Partez discrètement.")]
    [InlineData("//Aside// text", "Aparté texte")]
    public void MarkerCountMismatch_Fails(string en, string fr)
    {
        Assert.Contains(TranslationChecks.Markup, FailureIds(Restext(en, fr)));
    }

    [Fact]
    public void UnderscoresInsidePlaceholders_AreNotMarkers()
    {
        var verdict = Restext("Spend {icon:creepy_icon} now.", "Dépensez {icon:creepy_icon} maintenant.");

        Assert.Empty(verdict.Failures);
    }

    [Fact]
    public void HtmlTagMismatch_FailsInResx()
    {
        Assert.Contains(TranslationChecks.HtmlTags, FailureIds(Resx("A companion for <em>{0}</em>.", "Un compagnon pour {0}.")));
        Assert.Empty(Resx("A companion for <em>{0}</em>.", "Un compagnon pour <em>{0}</em>.").Failures);
    }

    [Fact]
    public void HtmlTags_AreNotCheckedInRestext()
    {
        Assert.DoesNotContain(TranslationChecks.HtmlTags, FailureIds(Restext("a <b>b</b>", "a b")));
    }

    [Fact]
    public void LineBreak_Fails()
    {
        Assert.Contains(TranslationChecks.MultiLine, FailureIds(Restext("One line", "Une\nligne")));
    }

    [Fact]
    public void Backslash_Fails()
    {
        Assert.Contains(TranslationChecks.Backslash, FailureIds(Restext("It's here", "C\\'est ici")));
    }

    [Theory]
    [InlineData(" lead", "tête")]
    [InlineData("lead", " tête")]
    [InlineData("trail ", "queue")]
    [InlineData("trail", "queue ")]
    public void EdgeWhitespaceMismatch_Fails(string en, string fr)
    {
        Assert.Contains(TranslationChecks.EdgeWhitespace, FailureIds(Restext(en, fr)));
    }

    [Fact]
    public void MatchingEdgeWhitespace_Passes()
    {
        Assert.DoesNotContain(TranslationChecks.EdgeWhitespace, FailureIds(Restext(" both ", " les deux ")));
    }

    [Theory]
    [InlineData("Your {playtxt} finances", "Vos {playtxt}financiers", "{playtxt}financiers")]
    [InlineData("Gain **4 VP** and go", "Gagnez **4 PV**et partez", "**et")]
    [InlineData("Read _this_ now", "Lisez _ceci_maintenant", "_maintenant")]
    [InlineData("Read //this// now", "Lisez //ceci//maintenant", "//maintenant")]
    public void LostSpaceAfterCloser_Fails(string en, string fr, string glued)
    {
        var failure = Assert.Single(Restext(en, fr).Failures, f => f.Check == TranslationChecks.SpaceAfterCloser);

        Assert.Contains(glued, failure.Detail);
    }

    [Fact]
    public void LostSpaceAfterClosingTag_FailsInResx()
    {
        Assert.Contains(TranslationChecks.SpaceAfterCloser, FailureIds(Resx("For <em>you</em> now", "Pour <em>vous</em>maintenant")));
    }

    [Fact]
    public void GluedCloser_ThatTheEnglishAlsoGlues_Passes()
    {
        Assert.Empty(Restext("The {n}th round", "La {n}e manche").Failures);
    }

    [Fact]
    public void OpeningMarkerBeforeAWord_IsNotAGluedCloser()
    {
        Assert.Empty(Restext("Gain **bold** text", "Gagnez **gras** texte").Failures);
    }

    [Theory]
    [InlineData("Read _this_ now", "Lisez _ ceci_ maintenant")]
    [InlineData("Read _this_ now", "Lisez _ceci _ maintenant")]
    public void SpaceJustInsideItalic_Fails(string en, string fr)
    {
        Assert.Contains(TranslationChecks.SpaceInsideItalic, FailureIds(Restext(en, fr)));
    }

    [Fact]
    public void FillInBlankUnderscores_ThatTheEnglishAlsoHas_Pass()
    {
        Assert.Empty(Restext("Citizens over the age of ___ serve.", "Les citoyens de plus de ___ ans servent.").Failures);
    }

    [Fact]
    public void UnnormalizedTypography_Fails()
    {
        var failure = Assert.Single(Restext("Ready?", "Prêt ?").Failures);

        Assert.Equal(TranslationChecks.Typography, failure.Check);
        Assert.Contains(FrenchTypography.NoSpaceBeforeHighPunctuation, failure.Detail);
    }

    [Fact]
    public void ForbiddenTerm_Fails()
    {
        var terms = TermBase.Parse("forbidden:\n  - 'Chef-d.œuvre'\n");

        var failure = Assert.Single(Restext("Your Masterwork", "Votre Chef-d'œuvre", terms).Failures);

        Assert.Equal(TranslationChecks.ForbiddenTerm, failure.Check);
        Assert.Contains("Chef-d'œuvre", failure.Detail);
    }

    [Fact]
    public void ForbiddenTerm_InsideAPlaceholder_IsIgnored()
    {
        var terms = TermBase.Parse("forbidden:\n  - 'Storybook'\n");

        Assert.Empty(Restext("See {Storybook}", "Voir {Storybook}", terms).Failures);
    }

    [Fact]
    public void MissingTerm_IsAFlagNotAFailure()
    {
        var terms = TermBase.Parse("terms:\n  - en: '\\bRounds?\\b'\n    fr: 'manche'\n");

        var verdict = Restext("End of Round", "Fin du tour", terms);

        Assert.Empty(verdict.Failures);
        Assert.Equal([TranslationChecks.TermMissing], FlagIds(verdict));
        Assert.Empty(Restext("End of Round", "Fin de la manche", terms).Flags);
    }

    [Fact]
    public void IdenticalToEnglish_IsFlagged()
    {
        Assert.Equal([TranslationChecks.IdenticalToEnglish], FlagIds(Restext("Advanced Weaponry", "Advanced Weaponry")));
        Assert.Empty(Restext("OK", "OK").Flags);
    }

    [Theory]
    [InlineData("This sentence is quite long indeed.", "Court.")]
    [InlineData("This sentence is quite long indeed.", "Cette phrase est vraiment très longue, beaucoup plus longue que prévu, sans raison.")]
    public void LengthRatioOutsideRange_IsFlagged(string en, string fr)
    {
        var verdict = Restext(en, fr);

        Assert.Empty(verdict.Failures);
        Assert.Equal([TranslationChecks.LengthRatio], FlagIds(verdict));
    }

    [Fact]
    public void ShortEnglish_SkipsTheLengthRatio()
    {
        Assert.Empty(Restext("Go", "Allons-y maintenant, vite").Flags);
    }
}
