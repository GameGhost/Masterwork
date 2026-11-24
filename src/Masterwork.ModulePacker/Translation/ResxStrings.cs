using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Masterwork.ModulePacker.Translation;

/// <summary>
/// Reads the string entries of a <c>.resx</c> file and rewrites their values in place. Works on the
/// raw text rather than a parsed document so a rewrite leaves everything else byte-for-byte as it
/// was: an XML round-trip would normalize the file's line endings and quoting.
/// </summary>
public static partial class ResxStrings
{
    // A <data> element holding a string: one with a type or mimetype attribute holds something else.
    [GeneratedRegex("""<data\s+name="(?<name>[^"]*)"(?<attrs>[^>]*)>\s*<value>(?<value>.*?)</value>""", RegexOptions.Singleline)]
    private static partial Regex DataPattern();

    /// <summary>Every string entry's name and decoded value, in file order.</summary>
    /// <exception cref="FormatException">A value isn't well-formed XML.</exception>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Entries(text))
        {
            result[entry.Name] = entry.Value;
        }

        return result;
    }

    /// <summary>Applies <paramref name="rewrite"/> to every string value, re-encoding only the ones it changes.</summary>
    /// <returns>The new file text and the names whose value changed.</returns>
    public static (string Text, IReadOnlyList<string> Changed) Rewrite(string text, Func<string, string, string> rewrite)
    {
        var sb = new StringBuilder(text.Length);
        var changed = new List<string>();
        var last = 0;
        foreach (var entry in Entries(text))
        {
            var updated = rewrite(entry.Name, entry.Value);
            if (updated == entry.Value)
            {
                continue;
            }

            sb.Append(text, last, entry.ValueGroup.Index - last);
            sb.Append(Encode(updated));
            last = entry.ValueGroup.Index + entry.ValueGroup.Length;
            changed.Add(entry.Name);
        }

        sb.Append(text, last, text.Length - last);
        return (sb.ToString(), changed);
    }

    private static IEnumerable<(string Name, string Value, Group ValueGroup)> Entries(string text)
    {
        foreach (Match m in DataPattern().Matches(text))
        {
            var attrs = m.Groups["attrs"].Value;
            if (attrs.Contains("type=", StringComparison.Ordinal))
            {
                continue;
            }

            yield return (Decode(m.Groups["name"].Value), Decode(m.Groups["value"].Value), m.Groups["value"]);
        }
    }

    private static string Decode(string raw)
    {
        try
        {
            return XElement.Parse($"<v xml:space=\"preserve\">{raw}</v>", LoadOptions.PreserveWhitespace).Value;
        }
        catch (XmlException ex)
        {
            throw new FormatException($"Resx value isn't well-formed XML: {raw}", ex);
        }
    }

    private static string Encode(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
