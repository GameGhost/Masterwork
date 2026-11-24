using System.Text;

namespace Masterwork.ModulePacker.Translation;

/// <summary>The result of normalizing one string's fr-CA typography.</summary>
/// <param name="Value">The normalized string; equal to the input when nothing needed fixing.</param>
/// <param name="Rules">A description of each rule that changed something, in a stable order.</param>
public sealed record TypographyResult(string Value, IReadOnlyList<string> Rules)
{
    public bool Changed => Rules.Count > 0;
}

/// <summary>
/// Normalizes fr-CA punctuation spacing: no space before <c>?</c> <c>!</c> <c>;</c>, a non-breaking
/// space before <c>:</c> and inside « ». Only spacing changes, so a fixed string keeps its origin.
/// Placeholders and (in resx) HTML tags are left alone, and so are a URL's <c>://</c> and a colon
/// between digits or placeholders, such as a time. A space just inside « » is kept even before
/// <c>?</c> or <c>!</c>, so a normalized string normalizes to itself.
/// </summary>
public static class FrenchTypography
{
    public const string NoSpaceBeforeHighPunctuation = "no space before ? ! ;";
    public const string NonBreakingSpaceBeforeColon = "non-breaking space before :";
    public const string NonBreakingSpaceInsideGuillemets = "non-breaking space inside « »";

    private const char Nbsp = '\u00A0';

    public static TypographyResult Normalize(string value, TranslationFileKind kind)
    {
        var text = new TranslationText(value, kind);
        var rules = new HashSet<string>();
        var sb = new StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (text.IsProtected(i))
            {
                sb.Append(c);
                continue;
            }

            if (IsSpace(c))
            {
                var end = i;
                while (end < value.Length && !text.IsProtected(end) && IsSpace(value[end]))
                {
                    end++;
                }

                var run = value[i..end];
                var next = end < value.Length && !text.IsProtected(end) ? value[end] : '\0';
                var afterGuillemet = i > 0 && !text.IsProtected(i - 1) && value[i - 1] == '«';
                if (next == '»' || afterGuillemet)
                {
                    AppendNbsp(sb, run, rules, NonBreakingSpaceInsideGuillemets);
                }
                else if (next is '?' or '!' or ';')
                {
                    rules.Add(NoSpaceBeforeHighPunctuation);
                }
                else if (next == ':' && IsSpacedColon(text, end))
                {
                    AppendNbsp(sb, run, rules, NonBreakingSpaceBeforeColon);
                }
                else
                {
                    sb.Append(run);
                }

                i = end - 1;
                continue;
            }

            // A space run before these was already handled above.
            var glued = i > 0 && !IsSpace(value[i - 1]);
            if (c == ':' && glued && IsSpacedColon(text, i))
            {
                rules.Add(NonBreakingSpaceBeforeColon);
                sb.Append(Nbsp);
            }
            else if (c == '»' && glued && value[i - 1] != '«')
            {
                rules.Add(NonBreakingSpaceInsideGuillemets);
                sb.Append(Nbsp);
            }

            sb.Append(c);
            if (c == '«' && i + 1 < value.Length && !IsSpace(value[i + 1]))
            {
                rules.Add(NonBreakingSpaceInsideGuillemets);
                sb.Append(Nbsp);
            }
        }

        string[] order = [NoSpaceBeforeHighPunctuation, NonBreakingSpaceBeforeColon, NonBreakingSpaceInsideGuillemets];
        return new TypographyResult(sb.ToString(), order.Where(rules.Contains).ToList());
    }

    private static bool IsSpace(char c) => c is ' ' or Nbsp or '\u202F' or '\t';

    // A colon that French spaces: not a URL's "://", not one after another colon, and not a time
    // or ratio, where a digit or placeholder sits on both sides ("12:30", "{0}:{1}").
    private static bool IsSpacedColon(TranslationText text, int index)
    {
        var value = text.Value;
        if (index == 0 || value[index - 1] == ':' || string.CompareOrdinal(value, index + 1, "//", 0, 2) == 0)
        {
            return false;
        }

        var numberBefore = char.IsDigit(value[index - 1]) || (value[index - 1] == '}' && text.IsProtected(index - 1));
        var numberAfter = index + 1 < value.Length
            && (char.IsDigit(value[index + 1]) || (value[index + 1] == '{' && text.IsProtected(index + 1)));
        return !(numberBefore && numberAfter);
    }

    private static void AppendNbsp(StringBuilder sb, string run, HashSet<string> rules, string rule)
    {
        if (run != Nbsp.ToString())
        {
            rules.Add(rule);
        }

        sb.Append(Nbsp);
    }
}
