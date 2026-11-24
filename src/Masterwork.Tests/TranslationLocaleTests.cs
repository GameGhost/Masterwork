using System.Text;
using Masterwork.Translation;

namespace Masterwork.Tests;

public sealed class TranslationLocaleTests
{
    private static StringVerdict Check(string locale, string source, string target, TermBase? terms = null) =>
        TranslationChecks.Check(source, target, TranslationFileKind.Restext, TranslationLocale.For(locale), terms);

    private static IEnumerable<string> FailureIds(StringVerdict v) => v.Failures.Select(f => f.Check);

    private static IEnumerable<string> FlagIds(StringVerdict v) => v.Flags.Select(f => f.Check);

    [Theory]
    [InlineData("fr-CA.restext", "fr-CA")]
    [InlineData("fr-CA.overrides.restext", "fr-CA")]
    [InlineData("en-US.common.restext", "en-US")]
    [InlineData("AppStrings.de-DE.resx", "de-DE")]
    [InlineData("modules/x/ja.restext", "ja")]
    [InlineData("AppStrings.resx", null)]
    [InlineData("translation.restext", null)]
    public void LocaleIsReadFromTheFileName(string path, string? expected)
    {
        Assert.Equal(expected, TranslationLocale.CodeFromFileName(path));
    }

    [Fact]
    public void MalformedCode_IsRejected()
    {
        Assert.Throws<FormatException>(() => TranslationLocale.For("French"));
    }

    [Fact]
    public void LocaleWithoutTypographyRules_SkipsTheTypographyCheck()
    {
        Assert.Empty(Check("de-DE", "Ready?", "Bereit ?").Failures);
        Assert.Contains(TranslationChecks.Typography, FailureIds(Check("fr-CA", "Ready?", "Prêt ?")));
    }

    [Fact]
    public void ScriptWithoutWordSpaces_AllowsAPlaceholderRunningIntoText()
    {
        Assert.Empty(Check("ja-JP", "You have {n} tokens.", "{n}個のトークンがあります。").Failures);
        Assert.Contains(TranslationChecks.SpaceAfterCloser, FailureIds(Check("fr-CA", "You have {n} tokens.", "Vous avez {n}jetons.")));
    }

    [Fact]
    public void LengthRatio_UsesTheLocalesRange()
    {
        const string source = "This sentence is quite long indeed.";
        const string japanese = "この文は本当に長いです。";

        Assert.DoesNotContain(TranslationChecks.LengthRatio, FlagIds(Check("ja-JP", source, japanese)));
        Assert.Contains(TranslationChecks.LengthRatio, FlagIds(Check("fr-CA", source, japanese)));
    }

    [Fact]
    public void LatinTextInANonLatinLocale_IsFlagged()
    {
        Assert.Contains(TranslationChecks.LatinScript, FlagIds(Check("ru-RU", "Advanced Weaponry", "Advanced Weaponry оружие")));
        Assert.DoesNotContain(TranslationChecks.LatinScript, FlagIds(Check("ru-RU", "Advanced Weaponry", "Передовое оружие")));
        Assert.DoesNotContain(TranslationChecks.LatinScript, FlagIds(Check("de-DE", "Weapons", "Waffen")));
    }

    [Fact]
    public void DecomposedTranslation_FailsAndIsComparedComposed()
    {
        var decomposed = "Votre Ope\u0301ra";
        var terms = TermBase.Parse("terms:\n  - source: 'Opera'\n    target: 'Opéra'\n");

        var verdict = Check("fr-CA", "Your Opera", decomposed, terms);

        Assert.Equal([TranslationChecks.UnicodeNormalization], FailureIds(verdict));
        Assert.Empty(verdict.Flags);
    }

    [Fact]
    public void Fix_ComposesThenAppliesTheLocalesTypography()
    {
        var fixedFr = TranslationChecks.Fix("Ope\u0301ra ?", TranslationFileKind.Restext, TranslationLocale.For("fr-CA"));
        var fixedDe = TranslationChecks.Fix("Ope\u0301ra ?", TranslationFileKind.Restext, TranslationLocale.For("de-DE"));

        Assert.Equal("Opéra?", fixedFr);
        Assert.Equal("Opéra ?", fixedDe);
        Assert.True(fixedDe.IsNormalized(NormalizationForm.FormC));
    }
}
