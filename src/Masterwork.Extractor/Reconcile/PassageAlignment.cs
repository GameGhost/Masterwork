using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace Masterwork.Extractor.Reconcile;

/// <summary>A passage flattened for alignment: header fields, then a token per text run or logic node.</summary>
internal sealed record PassageTree(string Id, IReadOnlyList<string> Tags, IReadOnlyList<(string Field, string Key)> Header, IReadOnlyList<AlignToken> Tokens)
{
    /// <summary>Every restext key the passage uses, in document order.</summary>
    public IEnumerable<string> Keys =>
        Header.Select(h => h.Key).Concat(Tokens.SelectMany(t => t.Keys));
}

/// <summary>
/// A text run (<see cref="Lines"/> set: the keys of consecutive text nodes, split at breaks) or a logic
/// node (<see cref="Signature"/> is its type plus identity fields; <see cref="Attached"/> its restext fields).
/// </summary>
internal sealed record AlignToken(string Signature, IReadOnlyList<IReadOnlyList<string>>? Lines, IReadOnlyList<(string Field, string Key)> Attached)
{
    public const string RunSignature = "RUN";

    public IEnumerable<string> Keys =>
        Lines is { } lines ? lines.SelectMany(l => l) : Attached.Select(a => a.Key);
}

/// <summary>How an English key group was paired with a French key group.</summary>
internal enum PairKind
{
    /// <summary>One English key, one French key.</summary>
    Single,

    /// <summary>A line or run whose fragment counts differ: the French must be split by hand.</summary>
    Split,
}

internal sealed record KeyPair(PairKind Kind, IReadOnlyList<string> English, IReadOnlyList<string> French);

/// <summary>One step of a passage alignment, in passage order: an English token, a French token, or both.</summary>
internal sealed record AlignStep(IReadOnlyList<string> English, IReadOnlyList<string> French);

internal sealed record PassageAlignmentResult(IReadOnlyList<KeyPair> Pairs, IReadOnlyList<AlignStep> Steps);

/// <summary>
/// Pairs an English passage's restext keys with the French passage of the same id by structure: logic
/// nodes are matched by longest common subsequence of their signatures, and each matched text run pairs
/// line by line, then fragment by fragment where both sides have the same number of fragments.
/// </summary>
internal static partial class PassageAlignment
{
    private static readonly string[] IdentityFields = ["target", "var", "asset", "passage", "layout", "match"];
    private static readonly string[] AttachFields = ["label", "okay", "cancel", "title", "subtitle", "expr", "if", "on", "match", "prompt", "placeholder"];
    private static readonly string[] HeaderFields = ["title", "subtitle"];

    [GeneratedRegex(@"restext://([A-Za-z0-9_\-]+)")]
    internal static partial Regex RestextRef();

    /// <summary>Loads every <c>*.mws.yaml</c> in <paramref name="dir"/>, keyed by passage id after <paramref name="renames"/>.</summary>
    public static Dictionary<string, PassageTree> LoadPassages(string dir, IReadOnlyDictionary<string, string> renames)
    {
        var passages = new Dictionary<string, PassageTree>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(dir, "*.mws.yaml").Order(StringComparer.Ordinal))
        {
            var stream = new YamlStream();
            using (var reader = new StreamReader(file))
            {
                stream.Load(reader);
            }

            var root = (YamlMappingNode)stream.Documents[0].RootNode;
            var id = Scalar(root, "passage_id") ?? Path.GetFileNameWithoutExtension(file);
            id = renames.GetValueOrDefault(id, id);
            var header = new List<(string, string)>();
            foreach (var field in HeaderFields)
            {
                if (Scalar(root, field) is { } v)
                {
                    foreach (Match m in RestextRef().Matches(v))
                    {
                        header.Add((field, m.Groups[1].Value));
                    }
                }
            }

            var tokens = new List<AlignToken>();
            if (root.Children.TryGetValue(new YamlScalarNode("nodes"), out var nodes) && nodes is YamlSequenceNode seq)
            {
                Flatten(seq, tokens, renames);
            }

            var tags = root.Children.TryGetValue(new YamlScalarNode("tags"), out var t) && t is YamlSequenceNode tagSeq
                ? tagSeq.Children.OfType<YamlScalarNode>().Select(s => s.Value ?? "").ToList()
                : [];
            passages[id] = new PassageTree(id, tags, header, tokens);
        }

        return passages;
    }

    private static string? Scalar(YamlMappingNode m, string key) =>
        m.Children.TryGetValue(new YamlScalarNode(key), out var v) && v is YamlScalarNode s ? s.Value : null;

    private static void Flatten(YamlSequenceNode seq, List<AlignToken> output, IReadOnlyDictionary<string, string> renames)
    {
        List<List<string>>? run = null;
        void FlushRun()
        {
            if (run != null)
            {
                output.Add(new AlignToken(AlignToken.RunSignature, run, []));
                run = null;
            }
        }

        foreach (var node in seq.Children.OfType<YamlMappingNode>())
        {
            var type = Scalar(node, "type");
            if (type == "text")
            {
                run ??= [[]];
                foreach (Match m in RestextRef().Matches(Scalar(node, "value") ?? ""))
                {
                    run[^1].Add(m.Groups[1].Value);
                }

                continue;
            }

            if (type == "break")
            {
                if (run != null && run[^1].Count > 0)
                {
                    run.Add([]);
                }

                continue;
            }

            FlushRun();
            if (type == null && node.Children.ContainsKey(new YamlScalarNode("match")))
            {
                type = "case";
            }

            // Restext keys differ between the languages, so they're masked out of the signature; a
            // literal switch-case match value stays, so randomly ordered cases don't cross-pair.
            var signature = new StringBuilder(type);
            foreach (var field in IdentityFields)
            {
                if (Scalar(node, field) is { } v)
                {
                    v = renames.GetValueOrDefault(v, v);
                    v = RestextRef().Replace(v, "R").Replace(".png", "", StringComparison.Ordinal);
                    signature.Append('|').Append(field).Append('=').Append(v);
                }
            }

            var attached = new List<(string, string)>();
            foreach (var field in AttachFields)
            {
                if (Scalar(node, field) is { } v)
                {
                    foreach (Match m in RestextRef().Matches(v))
                    {
                        attached.Add((field, m.Groups[1].Value));
                    }
                }
            }

            output.Add(new AlignToken(signature.ToString(), null, attached));

            // A case's body is scoped to its match value, or a skipped case token would let its
            // body align with another case's.
            var scope = type == "case" ? "@" + signature : "";
            foreach (var (k, v) in node.Children)
            {
                if (v is YamlSequenceNode child && child.Children.FirstOrDefault() is YamlMappingNode)
                {
                    var name = ((YamlScalarNode)k).Value;
                    output.Add(new AlignToken("OPEN:" + name + scope, null, []));
                    Flatten(child, output, renames);
                    output.Add(new AlignToken("CLOSE:" + name + scope, null, []));
                }
            }
        }

        FlushRun();
    }

    public static PassageAlignmentResult Align(PassageTree english, PassageTree french)
    {
        var pairs = new List<KeyPair>();
        var steps = new List<AlignStep>
        {
            new(english.Header.Select(h => h.Key).ToList(), french.Header.Select(h => h.Key).ToList()),
        };
        PairAttached(english.Header, french.Header, pairs);

        var a = english.Tokens;
        var b = french.Tokens;
        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
        {
            for (var j = b.Count - 1; j >= 0; j--)
            {
                lcs[i, j] = a[i].Signature == b[j].Signature
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        int x = 0, y = 0;
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && a[x].Signature == b[y].Signature)
            {
                if (a[x].Lines is { } en)
                {
                    PairRun(en, b[y].Lines!, pairs);
                }
                else
                {
                    PairAttached(a[x].Attached, b[y].Attached, pairs);
                }

                steps.Add(new AlignStep(a[x].Keys.ToList(), b[y].Keys.ToList()));
                x++;
                y++;
            }
            else if (y >= b.Count || (x < a.Count && lcs[x + 1, y] >= lcs[x, y + 1]))
            {
                steps.Add(new AlignStep(a[x].Keys.ToList(), []));
                x++;
            }
            else
            {
                steps.Add(new AlignStep([], b[y].Keys.ToList()));
                y++;
            }
        }

        return new PassageAlignmentResult(pairs, steps);
    }

    private static void PairAttached(IReadOnlyList<(string Field, string Key)> english, IReadOnlyList<(string Field, string Key)> french, List<KeyPair> pairs)
    {
        foreach (var group in english.GroupBy(t => t.Field))
        {
            var e = group.Select(t => t.Key).ToList();
            var f = french.Where(t => t.Field == group.Key).Select(t => t.Key).ToList();
            if (e.Count == f.Count)
            {
                for (var i = 0; i < e.Count; i++)
                {
                    pairs.Add(new KeyPair(PairKind.Single, [e[i]], [f[i]]));
                }
            }
            else if (f.Count > 0)
            {
                pairs.Add(new KeyPair(PairKind.Split, e, f));
            }
        }
    }

    private static void PairRun(IReadOnlyList<IReadOnlyList<string>> english, IReadOnlyList<IReadOnlyList<string>> french, List<KeyPair> pairs)
    {
        var en = english.Where(l => l.Count > 0).ToList();
        var fr = french.Where(l => l.Count > 0).ToList();
        if (en.Count == fr.Count)
        {
            for (var i = 0; i < en.Count; i++)
            {
                if (en[i].Count == fr[i].Count)
                {
                    for (var j = 0; j < en[i].Count; j++)
                    {
                        pairs.Add(new KeyPair(PairKind.Single, [en[i][j]], [fr[i][j]]));
                    }
                }
                else
                {
                    pairs.Add(new KeyPair(PairKind.Split, en[i], fr[i]));
                }
            }

            return;
        }

        var allEn = en.SelectMany(l => l).ToList();
        var allFr = fr.SelectMany(l => l).ToList();
        if (allEn.Count == allFr.Count)
        {
            // Same fragments, broken into lines differently.
            for (var j = 0; j < allEn.Count; j++)
            {
                pairs.Add(new KeyPair(PairKind.Single, [allEn[j]], [allFr[j]]));
            }
        }
        else if (allEn.Count > 0 && allFr.Count > 0)
        {
            pairs.Add(new KeyPair(PairKind.Split, allEn, allFr));
        }
    }
}
