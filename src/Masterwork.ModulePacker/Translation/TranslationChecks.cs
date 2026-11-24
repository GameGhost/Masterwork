using System.Text.RegularExpressions;

namespace Masterwork.ModulePacker.Translation;

/// <summary>
/// The deterministic checks on one translated string. Hard failures mean the string can't ship;
/// flags are handed to the judge, which may accept them (a proper name left in English, say).
/// </summary>
public static partial class TranslationChecks
{
    // Hard failures.
    public const string Empty = "empty";
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

    /// <summary>A French key the English file doesn't have; checked per file pair.</summary>
    public const string UnknownKey = "unknown-key";

    // Flags.
    public const string TermMissing = "term-missing";
    public const string IdenticalToEnglish = "identical-to-english";
    public const string LengthRatio = "length-ratio";

    /// <summary>An English key the French file doesn't have; checked per file pair.</summary>
    public const string Untranslated = "untranslated";

    /// <summary>Strings whose English text is at most this long skip the length-ratio flag.</summary>
    public const int LengthRatioMinEnglish = 20;
    public const double LengthRatioLow = 0.7;
    public const double LengthRatioHigh = 2.0;

    [GeneratedRegex("[A-Za-z]{3}")]
    private static partial Regex EnglishWordPattern();

    public static StringVerdict Check(string english, string french, TranslationFileKind kind, TermBase? termBase = null)
    {
        termBase ??= TermBase.Empty;
        var failures = new List<TranslationFinding>();
        var flags = new List<TranslationFinding>();

        if (french.Length == 0)
        {
            if (english.Length > 0)
            {
                failures.Add(new(Empty, "The French is empty."));
            }

            return new StringVerdict(failures, flags);
        }

        var en = new TranslationText(english, kind);
        var fr = new TranslationText(french, kind);

        CompareMultiset(failures, Placeholders, "placeholders", en.Placeholders, fr.Placeholders);
        foreach (var token in TranslationText.MarkerTokens)
        {
            var enCount = en.Markers.Count(m => m.Token == token);
            var frCount = fr.Markers.Count(m => m.Token == token);
            if (enCount != frCount)
            {
                failures.Add(new(Markup, $"'{token}' appears {enCount} times in the English, {frCount} in the French."));
            }
        }

        if (en.Blanks != fr.Blanks)
        {
            failures.Add(new(Markup, $"The English has {en.Blanks} fill-in blanks ('___'), the French {fr.Blanks}."));
        }

        if (kind == TranslationFileKind.Resx)
        {
            CompareMultiset(failures, HtmlTags, "HTML tags", en.Tags, fr.Tags);
        }

        if (french.Contains('\n') || french.Contains('\r'))
        {
            failures.Add(new(MultiLine, "The French contains a line break; strings are single-line."));
        }

        if (french.Contains('\\'))
        {
            failures.Add(new(Backslash, "The French contains a backslash; strings have no escapes."));
        }

        CheckEdgeWhitespace(failures, english, french);
        CheckSpaceAfterClosers(failures, en, fr);
        CheckSpaceInsideItalic(failures, en, fr);

        var typography = FrenchTypography.Normalize(french, kind);
        if (typography.Changed)
        {
            failures.Add(new(Typography, $"Needs {string.Join("; ", typography.Rules)}. The fixer corrects this."));
        }

        foreach (var forbidden in termBase.Forbidden)
        {
            var match = forbidden.Match(fr.Plain);
            if (match.Success)
            {
                failures.Add(new(ForbiddenTerm, $"'{match.Value}' is forbidden (pattern '{forbidden}')."));
            }
        }

        foreach (var term in termBase.Terms)
        {
            var enMatch = term.English.Match(en.Plain);
            if (enMatch.Success && !term.French.IsMatch(fr.Plain))
            {
                flags.Add(new(TermMissing, $"The English uses '{enMatch.Value}' but the French has no '{term.French}'."));
            }
        }

        if (EnglishWordPattern().IsMatch(en.Plain) && string.Equals(en.Plain, fr.Plain, StringComparison.Ordinal))
        {
            flags.Add(new(IdenticalToEnglish, "The French is the same as the English."));
        }

        if (en.Plain.Length > LengthRatioMinEnglish)
        {
            var ratio = (double)fr.Plain.Length / en.Plain.Length;
            if (ratio < LengthRatioLow || ratio > LengthRatioHigh)
            {
                flags.Add(new(LengthRatio, $"The French is {ratio:0.00}× the English length (expected {LengthRatioLow}–{LengthRatioHigh})."));
            }
        }

        return new StringVerdict(failures, flags);
    }

    private static void CompareMultiset(List<TranslationFinding> failures, string check, string what, IReadOnlyList<string> en, IReadOnlyList<string> fr)
    {
        var enSorted = en.Order(StringComparer.Ordinal).ToList();
        var frSorted = fr.Order(StringComparer.Ordinal).ToList();
        if (!enSorted.SequenceEqual(frSorted, StringComparer.Ordinal))
        {
            failures.Add(new(check, $"English {what} [{string.Join(" ", enSorted)}], French [{string.Join(" ", frSorted)}]."));
        }
    }

    private static void CheckEdgeWhitespace(List<TranslationFinding> failures, string english, string french)
    {
        static bool Leading(string s) => s.Length > 0 && char.IsWhiteSpace(s[0]);
        static bool Trailing(string s) => s.Length > 0 && char.IsWhiteSpace(s[^1]);
        if (Leading(english) != Leading(french))
        {
            failures.Add(new(EdgeWhitespace, Leading(english) ? "The English starts with whitespace; the French doesn't." : "The French starts with whitespace; the English doesn't."));
        }

        if (Trailing(english) != Trailing(french))
        {
            failures.Add(new(EdgeWhitespace, Trailing(english) ? "The English ends with whitespace; the French doesn't." : "The French ends with whitespace; the English doesn't."));
        }
    }

    // A placeholder, closing tag or closing marker running straight into a word, more often than the
    // English does it: "{playtxt}financiers", "**4 PV**et".
    private static void CheckSpaceAfterClosers(List<TranslationFinding> failures, TranslationText en, TranslationText fr)
    {
        var enGlued = Glued(en).Count;
        var frGlued = Glued(fr);
        if (frGlued.Count > enGlued)
        {
            failures.Add(new(SpaceAfterCloser, $"A space is missing after a placeholder or closing marker: {string.Join(", ", frGlued.Select(g => $"'{g}'"))}."));
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

    private static void CheckSpaceInsideItalic(List<TranslationFinding> failures, TranslationText en, TranslationText fr)
    {
        var frSpaced = SpacedItalics(fr);
        if (frSpaced.Count > SpacedItalics(en).Count)
        {
            failures.Add(new(SpaceInsideItalic, $"Whitespace just inside an italic '_' at position {string.Join(", ", frSpaced)}."));
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
}
