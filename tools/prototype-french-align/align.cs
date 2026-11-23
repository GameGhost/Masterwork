#:package YamlDotNet@18.1.0
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// PROTOTYPE — throwaway. Answers: how are Cradle French strings matched to the current English restext keys?
// Usage: dotnet run align.cs -- <enExtractDir> <frExtractDir> <moduleDir> <outDir>
//   enExtractDir / frExtractDir: unmodified extractor output (passages/ + en-US.restext) for each language
//   moduleDir: the live module (en-US.restext + en-US.overrides.restext) — the target key set
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

var (enDir, frDir, modDir, outDir) = (args[0], args[1], args[2], args[3]);
Directory.CreateDirectory(outDir);

var renames = new Dictionary<string, string>
{
    ["IntroMartial-Séparatistes"] = "IntroMartial-Separatists",
    ["MartialPre2"] = "Martia1Pre2",
    ["FP10PV"] = "FP10VP",
    ["FP10PVsignin"] = "FP10VPsignin",
    ["TCOD-NameEntryTownConfirm"] = "NameEntryTownConfirm",
};

var enText = Align.LoadRestext(Path.Combine(modDir, "en-US.restext"));
var overrides = Align.LoadRestext(Path.Combine(modDir, "en-US.overrides.restext"));
var frText = Align.LoadRestext(Path.Combine(frDir, "en-US.restext"));

var enPassages = Align.LoadPassages(Path.Combine(enDir, "passages"), renames);
var frPassages = Align.LoadPassages(Path.Combine(frDir, "passages"), renames);

var candidates = new Dictionary<string, List<Candidate>>();
var passageStats = new List<object>();
foreach (var (id, en) in enPassages)
{
    if (!frPassages.TryGetValue(id, out var fr)) { passageStats.Add(new { id, status = "no-french" }); continue; }
    var pairs = Align.AlignPassage(id, en, fr);
    foreach (var p in pairs)
        foreach (var k in p.EnKeys)
            (candidates.TryGetValue(k, out var l) ? l : candidates[k] = []).Add(new Candidate(id, p.Kind, p.EnKeys, p.FrKeys));
    passageStats.Add(new { id, status = "aligned", pairs = pairs.Count });
}

var results = new List<KeyResult>();
foreach (var (key, en) in enText.Concat(overrides))
    results.Add(Align.Classify(key, en, overrides.ContainsKey(key), candidates.GetValueOrDefault(key) ?? [], frText));

var summary = results.GroupBy(r => r.Class).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
Console.WriteLine($"keys: {results.Count}");
foreach (var (c, n) in summary) Console.WriteLine($"  {c,-22} {n,5}  {100.0 * n / results.Count:F1}%");
Console.WriteLine($"french passages with no english: {string.Join(", ", frPassages.Keys.Except(enPassages.Keys))}");
Console.WriteLine($"english passages with no french: {string.Join(", ", enPassages.Keys.Except(frPassages.Keys))}");

var json = JsonSerializer.Serialize(new { summary, results, passages = passageStats },
    new JsonSerializerOptions { WriteIndented = false, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
File.WriteAllText(Path.Combine(outDir, "matches.json"), json, new UTF8Encoding(false));
var shell = File.ReadAllText(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "report.html"));
File.WriteAllText(Path.Combine(outDir, "report.html"), shell.Replace("/*DATA*/null", json), new UTF8Encoding(false));

record Token(string Sig, List<List<string>>? Lines, List<(string Field, string Key)> Attached);
record Pair(string Kind, List<string> EnKeys, List<string> FrKeys);
record Candidate(string Passage, string Kind, List<string> EnGroup, List<string> FrGroup);
record KeyResult(string Key, string En, string? Fr, string Class, string Origin, List<string> Flags, List<string> Passages, List<string> Alternatives, List<string> SplitGroup);
record PassageTree(List<(string Field, string Key)> Header, List<Token> Tokens);

static class Align
{
    static readonly Regex RestextRef = new(@"restext://([A-Za-z0-9_\-]+)");
    static readonly Regex Placeholder = new(@"\{[^}]+\}");
    static readonly string[] IdentityFields = ["target", "var", "asset", "passage", "layout", "match"];
    static readonly string[] AttachFields = ["label", "okay", "cancel", "title", "subtitle", "expr", "if", "on", "match", "prompt", "placeholder"];

    public static Dictionary<string, string> LoadRestext(string path)
    {
        var d = new Dictionary<string, string>();
        if (!File.Exists(path)) return d;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var i = line.IndexOf('=');
            if (i > 0) d[line[..i]] = line[(i + 1)..];
        }
        return d;
    }

    public static Dictionary<string, PassageTree> LoadPassages(string dir, Dictionary<string, string> renames)
    {
        var d = new Dictionary<string, PassageTree>();
        foreach (var f in Directory.GetFiles(dir, "*.mws.yaml"))
        {
            var ys = new YamlStream();
            using (var r = new StreamReader(f)) ys.Load(r);
            var root = (YamlMappingNode)ys.Documents[0].RootNode;
            var id = Scalar(root, "passage_id")!;
            id = renames.GetValueOrDefault(id, id);
            var header = new List<(string, string)>();
            foreach (var fld in new[] { "title", "subtitle" })
                if (Scalar(root, fld) is { } v) foreach (Match m in RestextRef.Matches(v)) header.Add((fld, m.Groups[1].Value));
            var tokens = new List<Token>();
            if (root.Children.TryGetValue("nodes", out var nodes)) Flatten((YamlSequenceNode)nodes, tokens, renames);
            d[id] = new PassageTree(header, tokens);
        }
        return d;
    }

    static string? Scalar(YamlMappingNode m, string k) =>
        m.Children.TryGetValue(new YamlScalarNode(k), out var v) && v is YamlScalarNode s ? s.Value : null;

    // Text and break nodes collapse into one "run" token (lines split at breaks); every other node is a
    // logic token whose signature is its type plus identity fields, carrying its restext-bearing fields.
    static void Flatten(YamlSequenceNode seq, List<Token> outp, Dictionary<string, string> renames)
    {
        List<List<string>>? run = null;
        void FlushRun() { if (run != null) { outp.Add(new Token("RUN", run, [])); run = null; } }

        foreach (var n in seq.Children.OfType<YamlMappingNode>())
        {
            var type = Scalar(n, "type");
            if (type == "text")
            {
                run ??= [[]];
                foreach (Match m in RestextRef.Matches(Scalar(n, "value") ?? "")) run[^1].Add(m.Groups[1].Value);
                continue;
            }
            if (type == "break") { if (run != null && run[^1].Count > 0) run.Add([]); continue; }
            FlushRun();
            if (type == null && n.Children.ContainsKey("match")) type = "case";
            var sig = new StringBuilder(type);
            foreach (var f in IdentityFields)
                if (Scalar(n, f) is { } v)
                {
                    v = renames.GetValueOrDefault(v, v);
                    v = RestextRef.Replace(v, "R").Replace(".png", "");
                    sig.Append('|').Append(f).Append('=').Append(v);
                }
            var attached = new List<(string, string)>();
            foreach (var f in AttachFields)
                if (Scalar(n, f) is { } v) foreach (Match m in RestextRef.Matches(v)) attached.Add((f, m.Groups[1].Value));
            outp.Add(new Token(sig.ToString(), null, attached));
            foreach (var (k, v) in n.Children)
            {
                var key = ((YamlScalarNode)k).Value!;
                if (v is YamlSequenceNode child && child.Children.FirstOrDefault() is YamlMappingNode)
                {
                    outp.Add(new Token("OPEN:" + key, null, []));
                    Flatten(child, outp, renames);
                    outp.Add(new Token("CLOSE:" + key, null, []));
                }
            }
        }
        FlushRun();
    }

    public static List<Pair> AlignPassage(string id, PassageTree en, PassageTree fr)
    {
        var pairs = new List<Pair>();
        PairAttached(en.Header, fr.Header, pairs);
        var a = en.Tokens; var b = fr.Tokens;
        var dp = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                dp[i, j] = a[i].Sig == b[j].Sig ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        int x = 0, y = 0;
        while (x < a.Count && y < b.Count)
        {
            if (a[x].Sig == b[y].Sig)
            {
                if (a[x].Lines is { } el) PairRun(el, b[y].Lines!, pairs);
                else PairAttached(a[x].Attached, b[y].Attached, pairs);
                x++; y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
            else y++;
        }
        return pairs;
    }

    static void PairAttached(List<(string Field, string Key)> en, List<(string Field, string Key)> fr, List<Pair> pairs)
    {
        foreach (var g in en.GroupBy(t => t.Field))
        {
            var e = g.Select(t => t.Key).ToList();
            var f = fr.Where(t => t.Field == g.Key).Select(t => t.Key).ToList();
            if (e.Count == f.Count) for (var i = 0; i < e.Count; i++) pairs.Add(new Pair("field:" + g.Key, [e[i]], [f[i]]));
        }
    }

    // Equal line counts pair line by line; a line with equal fragment counts pairs key by key, otherwise
    // the line pairs as a group the translator must split. Unequal line counts pair the whole run.
    static void PairRun(List<List<string>> en, List<List<string>> fr, List<Pair> pairs)
    {
        en = en.Where(l => l.Count > 0).ToList(); fr = fr.Where(l => l.Count > 0).ToList();
        if (en.Count == fr.Count)
        {
            for (var i = 0; i < en.Count; i++)
                if (en[i].Count == fr[i].Count) for (var j = 0; j < en[i].Count; j++) pairs.Add(new Pair("text", [en[i][j]], [fr[i][j]]));
                else pairs.Add(new Pair("line-split", en[i], fr[i]));
        }
        else if (en.SelectMany(l => l).Count() == fr.SelectMany(l => l).Count())
        {
            var e = en.SelectMany(l => l).ToList(); var f = fr.SelectMany(l => l).ToList();
            for (var j = 0; j < e.Count; j++) pairs.Add(new Pair("text-rebroken", [e[j]], [f[j]]));
        }
        else if (en.Count > 0 && fr.Count > 0) pairs.Add(new Pair("run-split", en.SelectMany(l => l).ToList(), fr.SelectMany(l => l).ToList()));
    }

    // Count of placeholders or closing bold markers directly followed by a letter ("{nameC}n'a", "**15**des").
    static int GluedBoundaries(string s)
    {
        var n = Regex.Matches(s, @"\}[A-Za-zÀ-ÿ]").Count;
        var parts = s.Split("**");
        for (var i = 2; i < parts.Length; i += 2)
            if (parts[i].Length > 0 && char.IsLetter(parts[i][0])) n++;
        return n;
    }

    public static KeyResult Classify(string key, string en, bool isOverride, List<Candidate> cands, Dictionary<string, string> frText)
    {
        var passages = cands.Select(c => c.Passage).Distinct().ToList();
        var single = cands.Where(c => c.EnGroup.Count == 1 && c.FrGroup.Count == 1).ToList();
        if (single.Count > 0)
        {
            var byText = single.GroupBy(c => frText.GetValueOrDefault(c.FrGroup[0], "")).OrderByDescending(g => g.Count()).ToList();
            var fr = byText[0].Key;
            var flags = new List<string>();
            if (byText.Count > 1) flags.Add($"conflict:{byText.Count}");
            var ep = Placeholder.Matches(en).Select(m => m.Value).Order().ToList();
            var fp = Placeholder.Matches(fr).Select(m => m.Value).Order().ToList();
            if (!ep.SequenceEqual(fp)) flags.Add("placeholders");
            if (en.Length >= 20 && (fr.Length < en.Length * 0.8 || fr.Length > en.Length * 2.0)) flags.Add("length");
            if (fr.Length == 0) flags.Add("empty");
            if (fr == en && Regex.IsMatch(en, @"[A-Za-z]{4,}")) flags.Add("untranslated");
            if (GluedBoundaries(fr) > GluedBoundaries(en)) flags.Add("spacing");
            var cls = flags.Count == 0 ? "official" : "official-flagged";
            return new KeyResult(key, en, fr, cls, "official", flags, passages, byText.Skip(1).Select(g => g.Key).ToList(), []);
        }
        var split = cands.FirstOrDefault(c => c.FrGroup.Count > 0);
        if (split != null)
        {
            var frJoined = string.Join(" ", split.FrGroup.Select(k => frText.GetValueOrDefault(k, "")));
            return new KeyResult(key, en, frJoined, "needs-split", "official, split", [], passages, [], split.EnGroup);
        }
        return new KeyResult(key, en, null, isOverride ? "override-no-source" : "unmatched", "agent", [], passages, [], []);
    }
}
