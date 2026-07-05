namespace Masterwork.ModuleFormat;

/// <summary>
/// Parses an <c>en-US.restext</c> locale file: one <c>Key=Value</c> per line, single-line values
/// only. <c>#</c>-prefixed lines are comments; blank lines are ignored.
/// </summary>
public static class RestextFile
{
    /// <summary>Parses restext text into a key/value dictionary.</summary>
    /// <param name="text">The full contents of an <c>en-US.restext</c> file.</param>
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            result[line[..eq]] = line[(eq + 1)..];
        }
        return result;
    }
}
