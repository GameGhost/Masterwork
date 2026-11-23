using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Masterwork.ModuleFormat;

/// <summary>One problem <see cref="ModuleLogicLint.Check"/> found, worded for the person packing the module.</summary>
public sealed record LogicLintFinding(string Message);

/// <summary>
/// Pack-time check that a module's logic compares restext-sourced values by key, not by their text in
/// one language. Looks at the <c>expr</c>, <c>if</c>, <c>target</c> and <c>match</c> fields of every
/// passage and layout the module would load (a base passage replaced by one in
/// <c>passages-override/</c> is skipped) and reports:
/// <list type="bullet">
/// <item>a quoted literal (or plain <c>target</c>/<c>match</c> value) equal to any restext value of
/// the module or its asset packs, in any locale;</item>
/// <item>two distinct keys referenced from logic that share a default-locale value but differ in
/// another locale.</item>
/// </list>
/// Legitimate hits are exempted in the module's <see cref="AllowlistFileName"/>.
/// </summary>
public static partial class ModuleLogicLint
{
    /// <summary>The per-module exemption file, at the module root. Never packaged.</summary>
    public const string AllowlistFileName = "logic-lint.allowlist.yaml";

    private static readonly string[] LogicFields = ["expr", "if", "target", "match"];

    /// <summary>Lints the module at <paramref name="moduleDirectory"/>, also reading the restext of <paramref name="assetPackDirectories"/>.</summary>
    public static IReadOnlyList<LogicLintFinding> Check(string moduleDirectory, IReadOnlyList<string> assetPackDirectories)
    {
        var restext = RestextSet.Load(moduleDirectory, assetPackDirectories);
        var allowlist = Allowlist.Load(moduleDirectory);
        var uses = CollectLogicUses(moduleDirectory);

        var findings = new List<LogicLintFinding>();
        findings.AddRange(CheckLiterals(uses, restext, allowlist));
        findings.AddRange(CheckSharedKeys(uses, restext, allowlist));
        return findings;
    }

    private static IEnumerable<LogicLintFinding> CheckLiterals(IReadOnlyList<LogicUse> uses, RestextSet restext, Allowlist allowlist)
    {
        var reported = new HashSet<(string File, string Literal)>();
        foreach (var use in uses)
        {
            foreach (var literal in use.Literals)
            {
                if (literal.Length == 0 || literal.Contains("restext://", StringComparison.Ordinal) ||
                    allowlist.Literals.Contains(literal) || !reported.Add((use.Source, literal)))
                {
                    continue;
                }

                var matches = restext.KeysWithValue(literal);
                if (matches.Count == 0)
                {
                    continue;
                }

                var keys = string.Join(", ", matches.Select(m => $"{m.Key} ({m.Locale})"));
                yield return new LogicLintFinding(
                    $"{use.Source}: the literal {Quote(literal)} in '{use.Field}' equals the restext value of {keys}. " +
                    $"Compare by key instead (\"restext://{matches[0].Key}\"), or, if the match is intended, add " +
                    $"`- {YamlQuote(literal)}` under `literals:` in {AllowlistFileName}.");
            }
        }
    }

    private static IEnumerable<LogicLintFinding> CheckSharedKeys(IReadOnlyList<LogicUse> uses, RestextSet restext, Allowlist allowlist)
    {
        var referenced = uses.SelectMany(u => u.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var groups = referenced
            .Select(k => (Key: k, Value: restext.Resolve(restext.DefaultLocale, k)))
            .Where(k => k.Value is not null)
            .GroupBy(k => k.Value!, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var keys = group.Select(g => g.Key).ToList();
            for (var i = 0; i < keys.Count; i++)
            {
                for (var j = i + 1; j < keys.Count; j++)
                {
                    var (a, b) = (keys[i], keys[j]);
                    if (allowlist.SharedKeys.Contains((a, b)))
                    {
                        continue;
                    }

                    var locale = restext.Locales.FirstOrDefault(l =>
                        !string.Equals(restext.Resolve(l, a), restext.Resolve(l, b), StringComparison.Ordinal));
                    if (locale is null)
                    {
                        continue;
                    }

                    yield return new LogicLintFinding(
                        $"The restext keys {a} and {b} are both used in logic and share the {restext.DefaultLocale} value " +
                        $"{Quote(group.Key)}, but differ in {locale} ({Quote(restext.Resolve(locale, a)!)} vs " +
                        $"{Quote(restext.Resolve(locale, b)!)}), so a value stored through one won't equal the other there. " +
                        $"Use one key at every site, or, if that's intended, add `- [{YamlQuote(a)}, {YamlQuote(b)}]` under " +
                        $"`shared_keys:` in {AllowlistFileName}.");
                }
            }
        }
    }

    private static string Quote(string value) => $"\"{value}\"";

    private static string YamlQuote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    // One logic field's value, with the string literals it compares against and the restext keys it names.
    private sealed record LogicUse(string Source, string Field, IReadOnlyList<string> Literals, IReadOnlyList<string> Keys);

    private static List<LogicUse> CollectLogicUses(string moduleDirectory)
    {
        var (passagesDir, overrideDir) = ModuleLoader.ResolvePassageDirs(moduleDirectory);
        var overrideIds = new HashSet<string>(StringComparer.Ordinal);
        var overrideFiles = PassageFiles(overrideDir).ToList();
        foreach (var file in overrideFiles)
        {
            foreach (var doc in LoadDocuments(file))
            {
                if (PassageId(doc) is { } id)
                {
                    overrideIds.Add(id);
                }
            }
        }

        var baseFiles = PassageFiles(passagesDir);
        var layoutFiles = PassageFiles(Path.Combine(moduleDirectory, "layouts"));

        var uses = new List<LogicUse>();
        foreach (var (file, skipOverridden) in baseFiles.Select(f => (f, true))
                     .Concat(overrideFiles.Select(f => (f, false)))
                     .Concat(layoutFiles.Select(f => (f, false))))
        {
            var relative = Path.GetRelativePath(moduleDirectory, file).Replace('\\', '/');
            foreach (var doc in LoadDocuments(file))
            {
                var id = PassageId(doc);
                if (skipOverridden && id is not null && overrideIds.Contains(id))
                {
                    continue;
                }

                var where = id is null ? relative : $"{relative} ({id})";
                Walk(doc, where, uses);
            }
        }

        return uses;
    }

    // Top-level files only, the same set ModuleLoader reads.
    private static IEnumerable<string> PassageFiles(string? directory) =>
        directory is not null && Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.mws.yaml").Order(StringComparer.Ordinal)
            : [];

    private static IEnumerable<YamlNode> LoadDocuments(string file)
    {
        var stream = new YamlStream();
        using (var reader = new StringReader(File.ReadAllText(file)))
        {
            stream.Load(reader);
        }

        return stream.Documents.Select(d => d.RootNode);
    }

    private static string? PassageId(YamlNode root) =>
        root is YamlMappingNode map && map.TryGet("passage_id") is YamlScalarNode s ? s.Value : null;

    private static void Walk(YamlNode node, string where, List<LogicUse> uses)
    {
        switch (node)
        {
            case YamlMappingNode map:
                foreach (var (keyNode, value) in map.Children)
                {
                    if (keyNode is YamlScalarNode { Value: { } key } && LogicFields.Contains(key))
                    {
                        foreach (var scalar in Scalars(value))
                        {
                            uses.Add(ReadUse(where, key, scalar));
                        }

                        if (value is YamlScalarNode)
                        {
                            continue;
                        }
                    }

                    Walk(value, where, uses);
                }

                break;

            case YamlSequenceNode sequence:
                foreach (var child in sequence.Children)
                {
                    Walk(child, where, uses);
                }

                break;
        }
    }

    // A logic field holds one scalar, except `match`, which may also hold a list of them.
    private static IEnumerable<string> Scalars(YamlNode value) => value switch
    {
        YamlScalarNode { Value: { } s } => [s],
        YamlSequenceNode seq => seq.Children.OfType<YamlScalarNode>().Select(c => c.Value).OfType<string>(),
        _ => [],
    };

    private static LogicUse ReadUse(string where, string field, string value)
    {
        var keys = RestextKeyPattern().Matches(value).Select(m => m.Groups[1].Value).ToList();

        var isExpression = value.StartsWith("${", StringComparison.Ordinal) && value.EndsWith('}');
        List<string> literals;
        if (field is "expr" or "if")
        {
            literals = QuotedLiterals(isExpression ? value[2..^1] : value);
        }
        else if (isExpression)
        {
            literals = QuotedLiterals(value[2..^1]);
        }
        else if (field == "match")
        {
            // A match value is a pattern; its operator (`=yes`, `!=yes`) isn't part of the compared text,
            // and a numeric pattern compares numbers, not display text.
            var operand = PatternOperatorPattern().Replace(value, "");
            literals = int.TryParse(operand, out _) ? [] : [operand];
        }
        else
        {
            literals = [value];
        }

        return new LogicUse(where, field, literals.Distinct(StringComparer.Ordinal).ToList(), keys);
    }

    private static List<string> QuotedLiterals(string expression)
    {
        var literals = new List<string>();
        for (var i = 0; i < expression.Length; i++)
        {
            if (expression[i] != '"')
            {
                continue;
            }

            var literal = new StringBuilder();
            for (i++; i < expression.Length && expression[i] != '"'; i++)
            {
                if (expression[i] == '\\' && i + 1 < expression.Length)
                {
                    i++;
                }

                literal.Append(expression[i]);
            }

            literals.Add(literal.ToString());
        }

        return literals;
    }

    [GeneratedRegex(@"restext://([A-Za-z0-9_]+)")]
    private static partial Regex RestextKeyPattern();

    [GeneratedRegex(@"^(?:!=|>=|<=|=|>|<)")]
    private static partial Regex PatternOperatorPattern();

    // Every restext value the module could resolve, per locale: module overrides, then the module's own
    // file, then its asset packs (a later pack wins), falling back to the module's default locale.
    private sealed class RestextSet
    {
        private readonly Dictionary<string, List<IReadOnlyDictionary<string, string>>> _tiersByLocale = new(StringComparer.Ordinal);

        public required string DefaultLocale { get; init; }

        public IEnumerable<string> Locales => _tiersByLocale.Keys.Where(l => l != DefaultLocale).Order(StringComparer.Ordinal);

        public static RestextSet Load(string moduleDirectory, IReadOnlyList<string> assetPackDirectories)
        {
            var set = new RestextSet { DefaultLocale = ReadDefaultLocale(moduleDirectory) };
            var parser = new RestextFile();

            foreach (var dir in assetPackDirectories.Reverse().Prepend(moduleDirectory))
            {
                foreach (var file in RestextFiles(dir, overrides: true).Concat(RestextFiles(dir, overrides: false)))
                {
                    set.Tiers(file.Locale).Add(parser.Parse(File.ReadAllText(file.Path)));
                }
            }

            return set;
        }

        private static IEnumerable<(string Path, string Locale)> RestextFiles(string directory, bool overrides)
        {
            const string overrideSuffix = ".overrides.restext";
            return Directory.EnumerateFiles(directory, "*.restext", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(p => (Path: p, Name: Path.GetFileName(p)))
                .Where(f => f.Name.EndsWith(overrideSuffix, StringComparison.OrdinalIgnoreCase) == overrides)
                .Select(f => (f.Path, overrides ? f.Name[..^overrideSuffix.Length] : f.Name[..^".restext".Length]));
        }

        private List<IReadOnlyDictionary<string, string>> Tiers(string locale)
        {
            if (!_tiersByLocale.TryGetValue(locale, out var tiers))
            {
                _tiersByLocale[locale] = tiers = [];
            }

            return tiers;
        }

        public string? Resolve(string locale, string key)
        {
            foreach (var l in locale == DefaultLocale ? [locale] : new[] { locale, DefaultLocale })
            {
                if (_tiersByLocale.TryGetValue(l, out var tiers))
                {
                    foreach (var tier in tiers)
                    {
                        if (tier.TryGetValue(key, out var value))
                        {
                            return value;
                        }
                    }
                }
            }

            return null;
        }

        public List<(string Key, string Locale)> KeysWithValue(string value) =>
            [.. _tiersByLocale
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .SelectMany(t => t.Value.SelectMany(tier => tier).Where(e => e.Value == value).Select(e => (e.Key, Locale: t.Key)))
                .Distinct()];

        private static string ReadDefaultLocale(string moduleDirectory)
        {
            var manifest = Path.Combine(moduleDirectory, "manifest.yaml");
            if (!File.Exists(manifest))
            {
                return ModuleLocales.Default;
            }

            var root = LoadDocuments(manifest).FirstOrDefault() as YamlMappingNode;
            return root?.TryGet("default_locale") is YamlScalarNode { Value: { Length: > 0 } locale } ? locale : ModuleLocales.Default;
        }
    }

    private sealed class Allowlist
    {
        public HashSet<string> Literals { get; } = new(StringComparer.Ordinal);

        // Stored with the two keys in ordinal order, the order findings name them in.
        public HashSet<(string, string)> SharedKeys { get; } = [];

        public static Allowlist Load(string moduleDirectory)
        {
            var allowlist = new Allowlist();
            var path = Path.Combine(moduleDirectory, AllowlistFileName);
            if (!File.Exists(path) || LoadDocuments(path).FirstOrDefault() is not YamlMappingNode root)
            {
                return allowlist;
            }

            if (root.TryGet("literals") is YamlSequenceNode literals)
            {
                foreach (var literal in literals.Children.OfType<YamlScalarNode>())
                {
                    allowlist.Literals.Add(literal.Value ?? "");
                }
            }

            if (root.TryGet("shared_keys") is YamlSequenceNode pairs)
            {
                foreach (var pair in pairs.Children.OfType<YamlSequenceNode>())
                {
                    var keys = pair.Children.OfType<YamlScalarNode>().Select(k => k.Value ?? "").Order(StringComparer.Ordinal).ToList();
                    if (keys.Count == 2)
                    {
                        allowlist.SharedKeys.Add((keys[0], keys[1]));
                    }
                }
            }

            return allowlist;
        }
    }
}
