using System.Text.RegularExpressions;
using Masterwork.ModuleFormat;

namespace Masterwork.Extractor.Reconcile;

/// <summary>What reconciliation settled for one live key.</summary>
public enum KeyBucket
{
    /// <summary>Official French, as the script has it.</summary>
    Official,

    /// <summary>Official French after a mechanical fix: a restored space or a majority pick among whitespace/case variants.</summary>
    OfficialFixed,

    /// <summary>A shared key whose uses the French words differently; the majority is written for the agent to settle.</summary>
    Conflict,

    /// <summary>Official French the agent must revise: a split, or a placeholder, length or spacing mismatch.</summary>
    Draft,

    /// <summary>The French script carries the English text unchanged.</summary>
    EnglishInFrench,

    /// <summary>No French counterpart was found.</summary>
    Unmatched,

    /// <summary>An app-authored key with no Cradle source (the module's overrides).</summary>
    NoSource,
}

/// <summary>One live English key and the French reconciliation found for it.</summary>
/// <param name="French">The French written to the restext, or <c>null</c> when the key gets none.</param>
/// <param name="Reason">Why the key isn't plain official; <c>null</c> when it is.</param>
/// <param name="Candidates">Every distinct French text the key was paired with, when there was more than one.</param>
/// <param name="Passages">The English passages that use the key.</param>
public sealed record ReconciledKey(
    string Key,
    string English,
    string? French,
    KeyBucket Bucket,
    string? Reason,
    IReadOnlyList<string> Candidates,
    IReadOnlyList<string> Passages)
{
    /// <summary>The <c># origin:</c> value written above the key, or <c>null</c> when the key isn't written.</summary>
    public string? Origin => Bucket switch
    {
        KeyBucket.Official or KeyBucket.OfficialFixed => "official",
        KeyBucket.Conflict or KeyBucket.Draft => "draft",
        _ => null,
    };
}

/// <summary>French text with no English counterpart, and the English on either side of it.</summary>
public sealed record FrenchOnlyText(string Passage, string? EnglishBefore, string French, string? EnglishAfter);

/// <summary>A line of the live restext files: a comment (empty for a blank line), or a key.</summary>
public sealed record RestextLayoutLine(string? Comment, string? Key);

/// <summary>The outcome of reconciling one scenario's French extraction with its live English keys.</summary>
/// <param name="Keys">Every live key outside dev-note passages, in live file order.</param>
/// <param name="DevNoteKeys">Keys used only by dev-note passages, left out of output and coverage.</param>
/// <param name="Layout">The live restext files' comments and keys, in order, for writing the French file.</param>
public sealed record Reconciliation(
    IReadOnlyList<ReconciledKey> Keys,
    IReadOnlyList<FrenchOnlyText> FrenchOnly,
    IReadOnlyList<string> DevNoteKeys,
    IReadOnlyList<string> EnglishPassagesWithoutFrench,
    IReadOnlyList<string> FrenchPassagesWithoutEnglish,
    IReadOnlyList<RestextLayoutLine> Layout);

/// <summary>
/// Pairs a French Cradle extraction with a scenario's live English keys: passages by id (after the
/// rename table), keys by passage structure. See <c>docs/extractor.md</c>, "Reconcile".
/// </summary>
public static partial class Reconciler
{
    /// <summary>Passages the French scripts renamed, French id → English id.</summary>
    public static readonly IReadOnlyDictionary<string, string> CradleFrenchRenames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["IntroMartial-Séparatistes"] = "IntroMartial-Separatists",
        ["MartialPre2"] = "Martia1Pre2",
        ["FP10PV"] = "FP10VP",
        ["FP10PVsignin"] = "FP10VPsignin",
        ["TCOD-NameEntryTownConfirm"] = "NameEntryTownConfirm",
    };

    private const string ExtractionRestext = "en-US.restext";
    private const string OverridesRestext = "en-US.overrides.restext";

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"[A-Za-z]{4,}")]
    private static partial Regex EnglishWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Whether a passage id names an authoring note left in the Cradle scripts (<c>9Note</c>,
    /// <c>NoteFactions</c>, <c>HUBExample</c>, <c>Variable List FnP</c>, …). None is reachable in play;
    /// dev notes stay English and are out of coverage. A passage tagged <c>NOTE</c> is one too.
    /// </summary>
    public static bool IsDevNotePassage(string passageId) =>
        passageId.Contains("note", StringComparison.OrdinalIgnoreCase) ||
        passageId.Contains("example", StringComparison.OrdinalIgnoreCase) ||
        passageId.Replace(" ", "", StringComparison.Ordinal).StartsWith("VariableList", StringComparison.OrdinalIgnoreCase) ||
        passageId is "Extra Code" or "DECREESLIST" or "OUTDATEDFirstPara" or "FixIt";

    private static bool IsDevNote(PassageTree passage) =>
        IsDevNotePassage(passage.Id) || passage.Tags.Any(t => t.Equals("NOTE", StringComparison.OrdinalIgnoreCase));

    /// <summary>Reconciles a French extraction with the live module.</summary>
    /// <param name="englishExtractDir">A fresh English extraction (<c>passages/</c> and <c>en-US.restext</c>).</param>
    /// <param name="frenchExtractDir">The French extraction, same shape (its restext is named <c>en-US.restext</c> too).</param>
    /// <param name="moduleDir">The live module, whose <c>en-US.restext</c> and <c>en-US.overrides.restext</c> are the target keys.</param>
    /// <param name="renames">French passage id → English id; defaults to <see cref="CradleFrenchRenames"/>.</param>
    public static Reconciliation Run(string englishExtractDir, string frenchExtractDir, string moduleDir, IReadOnlyDictionary<string, string>? renames = null)
    {
        renames ??= CradleFrenchRenames;
        var noRenames = new Dictionary<string, string>();
        var enPassages = PassageAlignment.LoadPassages(Path.Combine(englishExtractDir, "passages"), noRenames);
        var frPassages = PassageAlignment.LoadPassages(Path.Combine(frenchExtractDir, "passages"), renames);
        var enText = LoadRestext(Path.Combine(englishExtractDir, ExtractionRestext));
        var frText = LoadRestext(Path.Combine(frenchExtractDir, ExtractionRestext));

        var layout = new List<RestextLayoutLine>();
        var live = new Dictionary<string, string>(StringComparer.Ordinal);
        ReadLayout(Path.Combine(moduleDir, ExtractionRestext), layout, live);
        ReadLayout(Path.Combine(moduleDir, OverridesRestext), layout, live);

        var usedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (id, tree) in enPassages)
        {
            foreach (var key in tree.Keys.Distinct())
            {
                AddTo(usedBy, key, id);
            }
        }

        var pairsByKey = new Dictionary<string, List<KeyPair>>(StringComparer.Ordinal);
        var frenchOnly = new List<FrenchOnlyText>();
        foreach (var (id, en) in enPassages.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (IsDevNote(en) || !frPassages.TryGetValue(id, out var fr))
            {
                continue;
            }

            var alignment = PassageAlignment.Align(en, fr);
            foreach (var pair in alignment.Pairs)
            {
                foreach (var key in pair.English)
                {
                    AddTo(pairsByKey, key, pair);
                }
            }

            CollectFrenchOnly(id, alignment, enText, frText, frenchOnly);
        }

        var frenchOnlyPassages = frPassages.Keys.Except(enPassages.Keys).Order(StringComparer.Ordinal).ToList();
        foreach (var id in frenchOnlyPassages.Where(id => !IsDevNote(frPassages[id])))
        {
            var text = JoinFrench(frPassages[id].Keys, frText);
            if (text.Length > 0)
            {
                frenchOnly.Add(new FrenchOnlyText(id, null, text, null));
            }
        }

        var keys = new List<ReconciledKey>();
        var devNoteKeys = new List<string>();
        foreach (var (key, english) in live)
        {
            var passages = usedBy.GetValueOrDefault(key) ?? [];
            if (passages.Count > 0 && passages.All(p => IsDevNote(enPassages[p])))
            {
                devNoteKeys.Add(key);
                continue;
            }

            keys.Add(Classify(key, english, passages, enText, frText, pairsByKey.GetValueOrDefault(key) ?? [], frPassages));
        }

        return new Reconciliation(
            keys,
            frenchOnly,
            devNoteKeys,
            enPassages.Keys.Except(frPassages.Keys).Order(StringComparer.Ordinal).ToList(),
            frenchOnlyPassages,
            layout);
    }

    private static void AddTo<T>(Dictionary<string, List<T>> lists, string key, T item)
    {
        if (!lists.TryGetValue(key, out var list))
        {
            lists[key] = list = [];
        }

        list.Add(item);
    }

    private static Dictionary<string, string> LoadRestext(string path) =>
        File.Exists(path)
            ? new Dictionary<string, string>(new RestextFile().Parse(File.ReadAllText(path)), StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static void ReadLayout(string path, List<RestextLayoutLine> layout, Dictionary<string, string> live)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                layout.Add(new RestextLayoutLine(line, null));
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq > 0 && live.TryAdd(line[..eq], line[(eq + 1)..]))
            {
                layout.Add(new RestextLayoutLine(null, line[..eq]));
            }
        }
    }

    private static ReconciledKey Classify(
        string key,
        string english,
        List<string> passages,
        Dictionary<string, string> enText,
        Dictionary<string, string> frText,
        List<KeyPair> pairs,
        Dictionary<string, PassageTree> frPassages)
    {
        ReconciledKey Result(KeyBucket bucket, string? french, string? reason, IReadOnlyList<string>? candidates = null) =>
            new(key, english, french, bucket, reason, candidates ?? [], passages);

        if (!enText.TryGetValue(key, out var extracted))
        {
            return Result(KeyBucket.NoSource, null, "no Cradle source");
        }

        var singles = pairs.Where(p => p.Kind == PairKind.Single).ToList();
        if (singles.Count == 0)
        {
            if (pairs.FirstOrDefault() is { } split)
            {
                var others = split.English.Where(k => k != key).ToList();
                var reason = others.Count > 0 ? $"needs split, shares French with {string.Join(", ", others)}" : "needs split";
                return Result(KeyBucket.Draft, JoinFrench(split.French, frText), reason);
            }

            return passages.Any(frPassages.ContainsKey)
                ? Result(KeyBucket.Unmatched, null, "no French counterpart in the passage")
                : Result(KeyBucket.Unmatched, null, "passage has no French");
        }

        var fixes = new List<string>();
        var fixedCandidates = singles.Select(p => frText.GetValueOrDefault(p.French[0], "")).Select(f => RestoreLostSpaces(f, english)).ToList();

        // A use the script left in English says nothing about the French wording of the others.
        if (fixedCandidates.Any(c => c.Text != english))
        {
            fixedCandidates.RemoveAll(c => c.Text == english);
        }

        if (fixedCandidates.Any(c => c.Fixed))
        {
            fixes.Add("restored a lost space");
        }

        var variants = fixedCandidates
            .Select((c, i) => (c.Text, i))
            .GroupBy(c => c.Text)
            .OrderByDescending(g => g.Count())
            .ThenByDescending(g => StartsLikeEnglish(g.Key, english))
            .ThenBy(g => g.Min(c => c.i))
            .Select(g => g.Key)
            .ToList();
        var french = variants[0];
        var conflict = variants.Count > 1 && variants.Select(NormalizeVariant).Distinct().Count() > 1;
        if (variants.Count > 1 && !conflict)
        {
            fixes.Add($"majority of {variants.Count} whitespace or case variants");
        }

        var drafts = new List<string>();

        if (french.Trim().Length == 0)
        {
            return Result(KeyBucket.Unmatched, null, "French is empty");
        }

        if (french == english && EnglishWord().IsMatch(Placeholder().Replace(english, "")))
        {
            return Result(KeyBucket.EnglishInFrench, null, "English left in the French script");
        }

        if (extracted != english)
        {
            drafts.Add("English changed since extraction");
        }

        if (!Placeholders(english).SequenceEqual(Placeholders(french)))
        {
            drafts.Add("placeholders differ");
        }

        if (english.Length >= 20 && (french.Length < english.Length * 0.8 || french.Length > english.Length * 2.0))
        {
            drafts.Add($"length ratio {(double)french.Length / english.Length:0.00}");
        }

        if (GluedBoundaries(french) > GluedBoundaries(english))
        {
            drafts.Add("missing space after a placeholder or bold");
        }

        var listedVariants = variants.Count > 1 ? variants : null;
        var conflictReason = $"French differs between uses ({variants.Count} variants)";
        if (drafts.Count > 0)
        {
            if (conflict)
            {
                drafts.Insert(0, conflictReason);
            }

            return Result(KeyBucket.Draft, french, string.Join("; ", drafts), listedVariants);
        }

        if (conflict)
        {
            return Result(KeyBucket.Conflict, french, conflictReason, listedVariants);
        }

        return fixes.Count > 0
            ? Result(KeyBucket.OfficialFixed, french, string.Join("; ", fixes), listedVariants)
            : Result(KeyBucket.Official, french, null);
    }

    /// <summary>Whether the first letters of both strings have the same case: the tie-break among variants.</summary>
    private static bool StartsLikeEnglish(string french, string english)
    {
        var f = french.FirstOrDefault(char.IsLetter);
        var e = english.FirstOrDefault(char.IsLetter);
        return f != default && e != default && char.IsUpper(f) == char.IsUpper(e);
    }

    private static string NormalizeVariant(string s) => Whitespace().Replace(s.Trim(), " ").ToLowerInvariant();

    private static List<string> Placeholders(string s) =>
        Placeholder().Matches(s).Select(m => m.Value).Order(StringComparer.Ordinal).ToList();

    private static string JoinFrench(IEnumerable<string> keys, Dictionary<string, string> frText) =>
        string.Join(" ", keys.Select(k => frText.GetValueOrDefault(k, "")).Where(t => t.Length > 0));

    /// <summary>
    /// Puts back the space the French script dropped after a placeholder or closing bold marker
    /// (<c>{nameC}n'a</c>, <c>**15**des</c>), wherever the English has a space there.
    /// </summary>
    internal static (string Text, bool Fixed) RestoreLostSpaces(string french, string english)
    {
        var result = french;
        foreach (var p in Placeholder().Matches(english).Select(m => m.Value).Distinct())
        {
            // The n-th French use of a placeholder follows the n-th English use when the counts agree.
            var enSpaced = Regex.Matches(english, Regex.Escape(p)).Select(m => m.Index + m.Length < english.Length && english[m.Index + m.Length] == ' ').ToList();
            var frEnds = Regex.Matches(result, Regex.Escape(p)).Select(m => m.Index + m.Length).ToList();
            for (var i = frEnds.Count - 1; i >= 0; i--)
            {
                var spaced = enSpaced.Count == frEnds.Count ? enSpaced[i] : enSpaced.All(s => s);
                if (spaced && frEnds[i] < result.Length && char.IsLetter(result[frEnds[i]]))
                {
                    result = result.Insert(frEnds[i], " ");
                }
            }
        }

        var enClosing = ClosingBoldEnds(english);
        var frClosing = ClosingBoldEnds(result);
        if (enClosing.Count == frClosing.Count)
        {
            for (var i = frClosing.Count - 1; i >= 0; i--)
            {
                var at = frClosing[i];
                var enAt = enClosing[i];
                if (at < result.Length && char.IsLetter(result[at]) && enAt < english.Length && english[enAt] == ' ')
                {
                    result = result.Insert(at, " ");
                }
            }
        }

        return (result, result != french);
    }

    /// <summary>The index just past each closing <c>**</c> (the 2nd, 4th, … marker).</summary>
    private static List<int> ClosingBoldEnds(string s)
    {
        var ends = new List<int>();
        var count = 0;
        for (var i = s.IndexOf("**", StringComparison.Ordinal); i >= 0; i = s.IndexOf("**", i + 2, StringComparison.Ordinal))
        {
            if (++count % 2 == 0)
            {
                ends.Add(i + 2);
            }
        }

        return ends;
    }

    private static int GluedBoundaries(string s)
    {
        var n = Placeholder().Matches(s).Count(m => m.Index + m.Length < s.Length && char.IsLetter(s[m.Index + m.Length]));
        return n + ClosingBoldEnds(s).Count(at => at < s.Length && char.IsLetter(s[at]));
    }

    private static void CollectFrenchOnly(
        string passage,
        PassageAlignmentResult alignment,
        Dictionary<string, string> enText,
        Dictionary<string, string> frText,
        List<FrenchOnlyText> output)
    {
        var paired = alignment.Pairs.SelectMany(p => p.French).ToHashSet(StringComparer.Ordinal);
        var sequence = alignment.Steps
            .SelectMany(s => s.English.Select(k => (English: true, Key: k)).Concat(s.French.Select(k => (English: false, Key: k))))
            .ToList();

        for (var i = 0; i < sequence.Count; i++)
        {
            if (sequence[i].English || paired.Contains(sequence[i].Key))
            {
                continue;
            }

            var group = new List<string>();
            var end = i;
            for (; end < sequence.Count && !sequence[end].English && !paired.Contains(sequence[end].Key); end++)
            {
                group.Add(sequence[end].Key);
            }

            var text = JoinFrench(group, frText);
            if (text.Trim().Length > 0)
            {
                var before = sequence.Take(i).LastOrDefault(e => e.English).Key;
                var after = sequence.Skip(end).FirstOrDefault(e => e.English).Key;
                output.Add(new FrenchOnlyText(
                    passage,
                    before is null ? null : enText.GetValueOrDefault(before),
                    text,
                    after is null ? null : enText.GetValueOrDefault(after)));
            }

            i = end - 1;
        }
    }
}
