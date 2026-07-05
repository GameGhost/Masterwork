namespace Masterwork.ModuleFormat;

/// <inheritdoc cref="IRestextFile"/>
public sealed class RestextFile : IRestextFile
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Parse(string text)
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
