using System.Text.RegularExpressions;

namespace Masterwork.Translation;

/// <summary>An emphasis marker found in a string's text, outside placeholders and tags.</summary>
/// <param name="Token">The marker as written: <c>**</c>, <c>_</c> or <c>//</c>.</param>
/// <param name="Index">Where it starts in the original string.</param>
/// <param name="IsCloser">True for the second, fourth, … occurrence of the same marker.</param>
internal readonly record struct Marker(string Token, int Index, bool IsCloser);

/// <summary>
/// Splits a translatable string into the spans the checks treat differently: <c>{placeholders}</c>
/// and, in resx, HTML tags are copied verbatim and never checked as prose; everything else is text.
/// </summary>
internal sealed partial class TranslationText
{
    public static readonly string[] MarkerTokens = ["**", "_", "//"];

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\{[^{}]*\}|</?[A-Za-z][^<>]*>")]
    private static partial Regex PlaceholderOrTagPattern();

    private readonly bool[] _protected;

    public TranslationText(string value, TranslationFileKind kind)
    {
        Value = value;
        _protected = new bool[value.Length];
        var pattern = kind == TranslationFileKind.Resx ? PlaceholderOrTagPattern() : PlaceholderPattern();
        var placeholders = new List<string>();
        var tags = new List<string>();
        var closers = new List<(int End, string Text)>();
        foreach (Match m in pattern.Matches(value))
        {
            Array.Fill(_protected, true, m.Index, m.Length);
            if (m.Value[0] == '{')
            {
                placeholders.Add(m.Value);
                closers.Add((m.Index + m.Length, m.Value));
            }
            else
            {
                tags.Add(m.Value);
                if (m.Value[1] == '/')
                {
                    closers.Add((m.Index + m.Length, m.Value));
                }
            }
        }

        Placeholders = placeholders;
        Tags = tags;
        Markers = FindMarkers();
        foreach (var marker in Markers.Where(m => m.IsCloser))
        {
            closers.Add((marker.Index + marker.Token.Length, marker.Token));
        }

        Closers = closers.OrderBy(c => c.End).ToList();
        Plain = string.Concat(Enumerable.Range(0, value.Length).Where(i => !_protected[i]).Select(i => value[i]));
    }

    public string Value { get; }

    /// <summary>Every <c>{placeholder}</c>, in order.</summary>
    public IReadOnlyList<string> Placeholders { get; }

    /// <summary>Every HTML tag (resx only), in order.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Emphasis markers. A run of two or more <c>_</c> is a fill-in blank, not markers.</summary>
    public IReadOnlyList<Marker> Markers { get; }

    /// <summary>How many fill-in blanks (<c>___</c>) the text has.</summary>
    public int Blanks { get; private set; }

    /// <summary>Where each placeholder, closing tag and closing marker ends, with its own text.</summary>
    public IReadOnlyList<(int End, string Text)> Closers { get; }

    /// <summary>The string with placeholders and tags removed: what a reader sees as prose.</summary>
    public string Plain { get; }

    public bool IsProtected(int index) => _protected[index];

    private List<Marker> FindMarkers()
    {
        var markers = new List<Marker>();
        var seen = MarkerTokens.ToDictionary(t => t, _ => 0);
        for (var i = 0; i < Value.Length; i++)
        {
            if (_protected[i])
            {
                continue;
            }

            if (Value[i] == '_' && i + 1 < Value.Length && Value[i + 1] == '_' && !_protected[i + 1])
            {
                while (i + 1 < Value.Length && Value[i + 1] == '_' && !_protected[i + 1])
                {
                    i++;
                }

                Blanks++;
                continue;
            }

            var token = MarkerTokens.FirstOrDefault(t => string.CompareOrdinal(Value, i, t, 0, t.Length) == 0
                && Enumerable.Range(i, t.Length).All(j => j < Value.Length && !_protected[j]));
            if (token is null)
            {
                continue;
            }

            markers.Add(new Marker(token, i, seen[token] % 2 == 1));
            seen[token]++;
            i += token.Length - 1;
        }

        return markers;
    }
}
