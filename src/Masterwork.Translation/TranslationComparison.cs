using System.Text.RegularExpressions;

namespace Masterwork.Translation;

/// <summary>
/// The comparisons between a source string and its translation that reconciliation and the
/// translation checks share, so the two always judge a string the same way. Placeholders and, in
/// resx, HTML tags never count as prose.
/// </summary>
public static partial class TranslationComparison
{
    /// <summary>Strings whose source prose is at most this long skip the length ratio.</summary>
    public const int LengthRatioMinSource = 20;

    [GeneratedRegex("[A-Za-z]{3}")]
    private static partial Regex SourceWordPattern();

    /// <summary>The string's placeholders, sorted, so two lists compare as multisets.</summary>
    public static IReadOnlyList<string> Placeholders(string value, TranslationFileKind kind) =>
        Placeholders(new TranslationText(value, kind));

    /// <summary>The string's prose: placeholders and tags removed.</summary>
    public static string Prose(string value, TranslationFileKind kind) => new TranslationText(value, kind).Plain;

    /// <summary>
    /// The translation's prose length as a multiple of the source's, when it's outside the locale's
    /// range; null when it's within range or the source is too short to judge.
    /// </summary>
    public static double? LengthRatioOutOfRange(string source, string target, TranslationFileKind kind, TranslationLocale locale) =>
        LengthRatioOutOfRange(new TranslationText(source, kind), new TranslationText(target, kind), locale);

    /// <summary>
    /// The places where the translation runs a placeholder, closing marker or closing tag straight
    /// into a word (<c>{playtxt}financiers</c>, <c>**4 PV**et</c>), when it does so more often than
    /// the source. Empty when it doesn't.
    /// </summary>
    public static IReadOnlyList<string> LostSpacesAfterClosers(string source, string target, TranslationFileKind kind) =>
        LostSpacesAfterClosers(new TranslationText(source, kind), new TranslationText(target, kind));

    /// <summary>Whether the translation's prose is the source's unchanged, and the source has a word in it.</summary>
    public static bool IsSourceUnchanged(string source, string target, TranslationFileKind kind) =>
        IsSourceUnchanged(new TranslationText(source, kind), new TranslationText(target, kind));

    /// <summary>
    /// Puts back the space a translation dropped after a placeholder, closing marker or closing tag,
    /// wherever the source has a space after the same one. The n-th use in the translation follows
    /// the n-th in the source when their counts agree; otherwise a space goes in only if the source
    /// spaces every use.
    /// </summary>
    public static string RestoreSpacesAfterClosers(string target, string source, TranslationFileKind kind)
    {
        var src = new TranslationText(source, kind);
        var tgt = new TranslationText(target, kind);
        var inserts = new SortedSet<int>();
        foreach (var group in tgt.Closers.GroupBy(c => c.Text))
        {
            var srcSpaced = src.Closers.Where(c => c.Text == group.Key).Select(c => c.End < source.Length && source[c.End] == ' ').ToList();
            var tgtEnds = group.Select(c => c.End).ToList();
            for (var i = 0; i < tgtEnds.Count; i++)
            {
                var spaced = srcSpaced.Count == tgtEnds.Count ? srcSpaced[i] : srcSpaced.Count > 0 && srcSpaced.All(s => s);
                if (spaced && tgtEnds[i] < target.Length && char.IsLetter(target[tgtEnds[i]]))
                {
                    inserts.Add(tgtEnds[i]);
                }
            }
        }

        var result = target;
        foreach (var at in inserts.Reverse())
        {
            result = result.Insert(at, " ");
        }

        return result;
    }

    internal static IReadOnlyList<string> Placeholders(TranslationText text) =>
        text.Placeholders.Order(StringComparer.Ordinal).ToList();

    internal static double? LengthRatioOutOfRange(TranslationText src, TranslationText tgt, TranslationLocale locale)
    {
        if (src.Plain.Length <= LengthRatioMinSource)
        {
            return null;
        }

        var ratio = (double)tgt.Plain.Length / src.Plain.Length;
        return ratio < locale.LengthRatioLow || ratio > locale.LengthRatioHigh ? ratio : null;
    }

    internal static IReadOnlyList<string> LostSpacesAfterClosers(TranslationText src, TranslationText tgt)
    {
        var tgtGlued = Glued(tgt);
        return tgtGlued.Count > Glued(src).Count ? tgtGlued : [];
    }

    internal static bool IsSourceUnchanged(TranslationText src, TranslationText tgt) =>
        SourceWordPattern().IsMatch(src.Plain) && string.Equals(src.Plain, tgt.Plain, StringComparison.Ordinal);

    private static List<string> Glued(TranslationText text)
    {
        var glued = new List<string>();
        foreach (var (end, closer) in text.Closers)
        {
            if (end < text.Value.Length && char.IsLetter(text.Value[end]))
            {
                glued.Add(closer + new string(text.Value[end..].TakeWhile(char.IsLetter).ToArray()));
            }
        }

        return glued;
    }
}
