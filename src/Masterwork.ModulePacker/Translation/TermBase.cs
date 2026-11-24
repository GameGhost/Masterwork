using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModulePacker.Translation;

/// <summary>An English term and the French the translation must use for it.</summary>
/// <param name="English">Matches the term in the English text.</param>
/// <param name="French">Matches any acceptable French rendering of it.</param>
public sealed record TermRule(Regex English, Regex French);

/// <summary>
/// The terms the checks enforce, read from one or more term-base YAML files. Every pattern is a .NET
/// regular expression, matched case-insensitively against the string's text outside placeholders
/// and tags. A plain word is a valid pattern.
/// <code>
/// terms:
///   - en: '\bRounds?\b'
///     fr: 'manche'
///     note: 'tour is a player turn'   # any other field is ignored
/// forbidden:
///   - 'Chef-d.œuvre'
/// </code>
/// </summary>
public sealed class TermBase
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static readonly TermBase Empty = new([], []);

    public TermBase(IReadOnlyList<TermRule> terms, IReadOnlyList<Regex> forbidden)
    {
        Terms = terms;
        Forbidden = forbidden;
    }

    /// <summary>English terms whose French must appear (a soft check).</summary>
    public IReadOnlyList<TermRule> Terms { get; }

    /// <summary>French the translation must never contain (a hard check).</summary>
    public IReadOnlyList<Regex> Forbidden { get; }

    /// <summary>Combines several term bases, such as the shared game terms and one scenario's own list.</summary>
    public static TermBase Merge(IEnumerable<TermBase> bases)
    {
        var all = bases.ToList();
        return new TermBase(all.SelectMany(b => b.Terms).ToList(), all.SelectMany(b => b.Forbidden).ToList());
    }

    /// <summary>Parses a term-base YAML file's text.</summary>
    /// <exception cref="FormatException">The file isn't shaped as documented, or a pattern isn't a valid regex.</exception>
    public static TermBase Parse(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new FormatException($"Term base isn't valid YAML: {ex.Message}", ex);
        }

        if (stream.Documents.Count == 0)
        {
            return Empty;
        }

        if (stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new FormatException("Term base must be a mapping with 'terms' and/or 'forbidden'.");
        }

        var terms = new List<TermRule>();
        foreach (var entry in Sequence(root, "terms"))
        {
            if (entry is not YamlMappingNode term)
            {
                throw new FormatException($"Each entry under 'terms' must be a mapping with 'en' and 'fr' (line {entry.Start.Line}).");
            }

            terms.Add(new TermRule(Pattern(term, "en"), Pattern(term, "fr")));
        }

        var forbidden = Sequence(root, "forbidden")
            .Select(n => n is YamlScalarNode s && !string.IsNullOrEmpty(s.Value)
                ? Compile(s.Value, s.Start.Line)
                : throw new FormatException($"Each entry under 'forbidden' must be a pattern (line {n.Start.Line})."))
            .ToList();

        return new TermBase(terms, forbidden);
    }

    private static IEnumerable<YamlNode> Sequence(YamlMappingNode root, string key)
    {
        if (!root.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return [];
        }

        return node is YamlSequenceNode seq
            ? seq.Children
            : throw new FormatException($"'{key}' must be a list (line {node.Start.Line}).");
    }

    private static Regex Pattern(YamlMappingNode term, string key)
    {
        if (!term.Children.TryGetValue(new YamlScalarNode(key), out var node)
            || node is not YamlScalarNode { Value: { Length: > 0 } value })
        {
            throw new FormatException($"Term at line {term.Start.Line} needs a non-empty '{key}' pattern.");
        }

        return Compile(value, node.Start.Line);
    }

    private static Regex Compile(string pattern, long line)
    {
        try
        {
            return new Regex(pattern, Options);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException($"Pattern '{pattern}' at line {line} isn't a valid regular expression: {ex.Message}", ex);
        }
    }
}
