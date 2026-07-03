using System;
using System.Collections.Generic;

namespace Masterwork.ModuleFormat;

// Parses an en-US.restext locale file: one `Key=Value` per line, single-line values only.
// `#`-prefixed lines are comments; blank lines are ignored.
public static class RestextFile
{
    public static IReadOnlyDictionary<string, string> Parse(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            result[line[..eq]] = line[(eq + 1)..];
        }
        return result;
    }
}
