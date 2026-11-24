using System.Text;

namespace Masterwork.ModulePacker.Translation;

/// <summary>
/// Line-level work on a <c>.restext</c> file that <see cref="Masterwork.ModuleFormat.RestextFile"/>'s
/// parser doesn't expose: rewriting values while keeping comments (such as <c># origin:</c>) and
/// line endings exactly, and finding lines the parser would skip.
/// </summary>
public static class RestextLines
{
    /// <summary>Applies <paramref name="rewrite"/> to every entry's value; comments and blank lines are untouched.</summary>
    /// <returns>The new file text and the keys whose value changed.</returns>
    public static (string Text, IReadOnlyList<string> Changed) Rewrite(string text, Func<string, string> rewrite)
    {
        var sb = new StringBuilder(text.Length);
        var changed = new List<string>();
        foreach (var (line, ending) in Lines(text))
        {
            var eq = line.IndexOf('=');
            if (IsNotCommentOrBlank(line) && eq >= 0)
            {
                var key = line[..eq];
                var value = line[(eq + 1)..];
                var updated = rewrite(value);
                if (updated != value)
                {
                    sb.Append(key).Append('=').Append(updated).Append(ending);
                    changed.Add(key);
                    continue;
                }
            }

            sb.Append(line).Append(ending);
        }

        return (sb.ToString(), changed);
    }

    /// <summary>1-based numbers of lines that are neither comments, blank, nor <c>Key=Value</c>: usually a value broken across lines.</summary>
    public static IReadOnlyList<int> MalformedLines(string text) =>
        Lines(text)
            .Select((l, i) => (l.Line, Number: i + 1))
            .Where(l => IsNotCommentOrBlank(l.Line) && !l.Line.Contains('='))
            .Select(l => l.Number)
            .ToList();

    private static bool IsNotCommentOrBlank(string line) => line.Length > 0 && !line.StartsWith('#');

    private static IEnumerable<(string Line, string Ending)> Lines(string text)
    {
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            if (nl < 0)
            {
                yield return (text[start..], "");
                yield break;
            }

            var end = nl > start && text[nl - 1] == '\r' ? nl - 1 : nl;
            yield return (text[start..end], text[end..(nl + 1)]);
            start = nl + 1;
        }
    }
}
