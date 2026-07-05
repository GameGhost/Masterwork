using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.ModuleFormat;

/// <inheritdoc cref="IRestextFile"/>
public sealed class RestextFile : IRestextFile
{
    private readonly ILogger<RestextFile> _logger;

    /// <summary>Creates a restext parser that discards log output.</summary>
    public RestextFile() : this(NullLogger<RestextFile>.Instance)
    {
    }

    /// <summary>Creates a restext parser that logs through <paramref name="logger"/>.</summary>
    public RestextFile(ILogger<RestextFile> logger)
    {
        _logger = logger;
    }

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
                _logger.LogWarning("Restext line has no '=' separator, skipping it: {Line}", line);
                continue;
            }

            result[line[..eq]] = line[(eq + 1)..];
        }

        _logger.LogDebug("Parsed restext file: {Count} entries", result.Count);
        return result;
    }
}
