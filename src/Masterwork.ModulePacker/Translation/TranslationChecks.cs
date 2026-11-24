using System.Text;
using System.Text.RegularExpressions;

namespace Masterwork.ModulePacker.Translation;

/// <summary>
/// The deterministic checks on one translated string against its source (the canonical English).
/// Hard failures mean the string can't ship; flags are handed to the judge, which may accept them
/// (a proper name left in English, say). Language-dependent rules come from the target's
/// <see cref="TranslationLocale"/>.
/// </summary>
public static partial class TranslationChecks
{
    // Hard failures.
    public const string Empty = "empty";
    public const string UnicodeNormalization = "unicode-normalization";
    public const string Placeholders = "placeholders";
    public const string Markup = "markup";
    public const string HtmlTags = "html-tags";
    public const string MultiLine = "multi-line";
    public const string Backslash = "backslash";
    public const string EdgeWhitespace = "edge-whitespace";
    public const string SpaceAfterCloser = "space-after-closer";
    public const string SpaceInsideItalic = "space-inside-italic";
    public const string Typography = "typography";
    public const string ForbiddenTerm = "forbidden-term";

    /// <summary>A target key the source file doesn't have; checked per file pair.</summary>
    public const string UnknownKey = "unknown-key";

    // Flags.
    public const string TermMissing = "term-missing";
    public const string IdenticalToSource = "identical-to-source";
    public const string LatinScript = "latin-script";
    public const string LengthRatio = "length-ratio";

    /// <summary>A source key the target file doesn't have; checked per file pair.</summary>
    public const string Untranslated = "untranslated";

    /// <summary>Strings whose source text is at most this long skip the length-ratio flag.</summary>
    public const int LengthRatioMinSource = 20;

    [GeneratedRegex("[A-Za-z]{3}")]
    private static partial Regex SourceWordPattern();

    public static StringVerdict Check(string source, string target, TranslationFileKind kind, TranslationLocale locale, TermBase? termBase = null)
    {
        termBase ??= TermBase.Empty;
        var failures = new List<TranslationFinding>();
        var flags = new List<TranslationFinding>();

        if (target.Length == 0)
        {
            if (source.Length > 0)
            {
                failures.Add(new(Empty, "The translation is empty."));
            }

            return new StringVerdict(failures, flags);
        }

        // Everything after this compares composed forms, so a decomposed accent can't hide a term.
        if (!target.IsNormalized(NormalizationForm.FormC))
        {
            failures.Add(new(UnicodeNormalization, "The translation isn't in Unicode NFC (composed characters). The fixer corrects this."));
            target = target.Normalize(NormalizationForm.FormC);
        }

        source = source.Normalize(NormalizationForm.FormC);
        var src = new TranslationText(source, kind);
        var tgt = new TranslationText(target, kind);

        CompareMultiset(failures, Placeholders, "placeholders", src.Placeholders, tgt.Placeholders);
        foreach (var token in TranslationText.MarkerTokens)
        {
            var srcCount = src.Markers.Count(m => m.Token == token);
            var tgtCount = tgt.Markers.Count(m => m.Token == token);
            if (srcCount != tgtCount)
            {
                failures.Add(new(Markup, $"'{token}' appears {srcCount} times in the source, {tgtCount} in the translation."));
            }
        }

        if (src.Blanks != tgt.Blanks)
        {
            failures.Add(new(Markup, $"The source has {src.Blanks} fill-in blanks ('___'), the translation {tgt.Blanks}."));
        }

        if (kind == TranslationFileKind.Resx)
        {
            CompareMultiset(failures, HtmlTags, "HTML tags", src.Tags, tgt.Tags);
        }

        if (target.Contains('\n') || target.Contains('\r'))
        {
            failures.Add(new(MultiLine, "The translation contains a line break; strings are single-line."));
        }

        if (target.Contains('\\'))
        {
            failures.Add(new(Backslash, "The translation contains a backslash; strings have no escapes."));
        }

        CheckEdgeWhitespace(failures, source, target);
        if (locale.SeparatesWords)
        {
            CheckSpaceAfterClosers(failures, src, tgt);
        }

        CheckSpaceInsideItalic(failures, src, tgt);

        var typography = locale.Typography?.Invoke(target, kind);
        if (typography is { Changed: true })
        {
            failures.Add(new(Typography, $"Needs {string.Join("; ", typography.Rules)}. The fixer corrects this."));
        }

        foreach (var forbidden in termBase.Forbidden)
        {
            var match = forbidden.Match(tgt.Plain);
            if (match.Success)
            {
                failures.Add(new(ForbiddenTerm, $"'{match.Value}' is forbidden (pattern '{forbidden}')."));
            }
        }

        foreach (var term in termBase.Terms)
        {
            var srcMatch = term.Source.Match(src.Plain);
            if (srcMatch.Success && !term.Target.IsMatch(tgt.Plain))
            {
                flags.Add(new(TermMissing, $"The source uses '{srcMatch.Value}' but the translation has no '{term.Target}'."));
            }
        }

        if (SourceWordPattern().IsMatch(src.Plain) && string.Equals(src.Plain, tgt.Plain, StringComparison.Ordinal))
        {
            flags.Add(new(IdenticalToSource, "The translation is the same as the source."));
        }

        if (!locale.LatinScript)
        {
            CheckScript(flags, tgt, locale);
        }

        if (src.Plain.Length > LengthRatioMinSource)
        {
            var ratio = (double)tgt.Plain.Length / src.Plain.Length;
            if (ratio < locale.LengthRatioLow || ratio > locale.LengthRatioHigh)
            {
                flags.Add(new(LengthRatio, $"The translation is {ratio:0.00}× the source length (expected {locale.LengthRatioLow}–{locale.LengthRatioHigh} for {locale.Code})."));
            }
        }

        return new StringVerdict(failures, flags);
    }

    /// <summary>What <c>--fix</c> writes: the value in NFC, then the locale's typography. Never changes the wording.</summary>
    public static string Fix(string target, TranslationFileKind kind, TranslationLocale locale)
    {
        var composed = target.Normalize(NormalizationForm.FormC);
        return locale.Typography?.Invoke(composed, kind).Value ?? composed;
    }

    private static void CompareMultiset(List<TranslationFinding> failures, string check, string what, IReadOnlyList<string> src, IReadOnlyList<string> tgt)
    {
        var srcSorted = src.Order(StringComparer.Ordinal).ToList();
        var tgtSorted = tgt.Order(StringComparer.Ordinal).ToList();
        if (!srcSorted.SequenceEqual(tgtSorted, StringComparer.Ordinal))
        {
            failures.Add(new(check, $"Source {what} [{string.Join(" ", srcSorted)}], translation [{string.Join(" ", tgtSorted)}]."));
        }
    }

    private static void CheckEdgeWhitespace(List<TranslationFinding> failures, string source, string target)
    {
        static bool Leading(string s) => s.Length > 0 && char.IsWhiteSpace(s[0]);
        static bool Trailing(string s) => s.Length > 0 && char.IsWhiteSpace(s[^1]);
        if (Leading(source) != Leading(target))
        {
            failures.Add(new(EdgeWhitespace, Leading(source) ? "The source starts with whitespace; the translation doesn't." : "The translation starts with whitespace; the source doesn't."));
        }

        if (Trailing(source) != Trailing(target))
        {
            failures.Add(new(EdgeWhitespace, Trailing(source) ? "The source ends with whitespace; the translation doesn't." : "The translation ends with whitespace; the source doesn't."));
        }
    }

    // A placeholder, closing tag or closing marker running straight into a word, more often than the
    // source does it: "{playtxt}financiers", "**4 PV**et".
    private static void CheckSpaceAfterClosers(List<TranslationFinding> failures, TranslationText src, TranslationText tgt)
    {
        var srcGlued = Glued(src).Count;
        var tgtGlued = Glued(tgt);
        if (tgtGlued.Count > srcGlued)
        {
            failures.Add(new(SpaceAfterCloser, $"A space is missing after a placeholder or closing marker: {string.Join(", ", tgtGlued.Select(g => $"'{g}'"))}."));
        }
    }

    private static List<string> Glued(TranslationText text)
    {
        var glued = new List<string>();
        foreach (var (end, closer) in text.Closers)
        {
            if (end < text.Value.Length && char.IsLetter(text.Value[end]))
            {
                var word = new string(text.Value[end..].TakeWhile(char.IsLetter).ToArray());
                glued.Add(closer + word);
            }
        }

        return glued;
    }

    private static void CheckSpaceInsideItalic(List<TranslationFinding> failures, TranslationText src, TranslationText tgt)
    {
        var tgtSpaced = SpacedItalics(tgt);
        if (tgtSpaced.Count > SpacedItalics(src).Count)
        {
            failures.Add(new(SpaceInsideItalic, $"Whitespace just inside an italic '_' at position {string.Join(", ", tgtSpaced)}."));
        }
    }

    private static List<int> SpacedItalics(TranslationText text) =>
        text.Markers
            .Where(m => m.Token == "_")
            .Where(m =>
            {
                var inside = m.IsCloser ? m.Index - 1 : m.Index + 1;
                return inside >= 0 && inside < text.Value.Length && char.IsWhiteSpace(text.Value[inside]);
            })
            .Select(m => m.Index)
            .ToList();

    // Mostly Latin letters in a locale written in another script: likely left untranslated in part.
    private static void CheckScript(List<TranslationFinding> flags, TranslationText tgt, TranslationLocale locale)
    {
        var letters = tgt.Plain.Where(char.IsLetter).ToList();
        var latin = letters.Count(c => c <= '\u024F');
        if (letters.Count >= 3 && latin * 2 > letters.Count)
        {
            flags.Add(new(LatinScript, $"{latin} of {letters.Count} letters are Latin, but {locale.Code} uses another script."));
        }
    }
}
