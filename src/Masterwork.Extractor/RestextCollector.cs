using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Masterwork.Extractor;

/// <summary>
/// Walks a passage's ToDict() output, extracts human-readable strings into
/// restext entries (Key=Value format), and mutates the dict in-place to replace
/// those strings with restext://Key references.
///
/// Key assignment is two-phase:
///   Phase 1 (CollectPassage calls) — keys are {PassageId}_{NNN}; global dedup prevents duplicates.
///   Phase 2 (BuildRenameMap + ApplyRenames) — keys used in 2+ passages are renamed to Common_{NNN}
///     and their entries moved to a "(Common)" group prepended to Passages.
/// </summary>
public sealed partial class RestextCollector
{
    public sealed record Entry(string Key, string Value);

    private readonly List<(string FileName, List<Entry> Entries)> _passages = [];
    private List<Entry> _current = [];
    private readonly Dictionary<string, string> _globalValueToKey = [];  // value → key, global dedup
    private readonly Dictionary<string, HashSet<string>> _keyPassages = [];  // key → passage IDs that use it
    private int _reuseCount;
    private int _commonCounter = 1;
    private string _passageId = "";
    private int _counter;

    // Called once per passage before serialization.
    // Transforms the V2Serializer.ToDict() output in-place; accumulates entries for this passage.
    public void CollectPassage(string passageId, string fileName, Dictionary<string, object?> passageDict)
    {
        _passageId = passageId;
        _counter = 1;
        _current = [];

        // v0.2: location.name is a passage-level header, not a node
        if (passageDict.TryGetValue("location", out var locObj) && locObj is Dictionary<string, object?> loc)
            ExtractField(loc, "name");

        if (passageDict.TryGetValue("nodes", out var nodes))
            WalkNodeList(nodes);

        if (_current.Count > 0)
            _passages.Add((fileName, _current));
    }

    // Returns all entries grouped by passage, in passage order.
    // After ApplyRenames: index 0 is the Common group (if any), followed by per-passage groups.
    public IReadOnlyList<(string FileName, List<Entry> Entries)> Passages => _passages;

    // Build a lookup map Key → Value for comment injection.
    public Dictionary<string, string> BuildCommentMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, entries) in _passages)
            foreach (var e in entries)
                map[e.Key] = e.Value;
        return map;
    }

    // ── Phase 2: rename multi-passage keys to Common_NNN ──────────────────

    // Returns a map of old key → Common_NNN for every key referenced in 2+ passages.
    // Call after all CollectPassage calls are done.
    public Dictionary<string, string> BuildRenameMap()
    {
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, passages) in _keyPassages.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (passages.Count > 1)
            {
                renames[key] = $"Common_{_commonCounter:D3}";
                _commonCounter++;
            }
        }
        return renames;
    }

    // Applies the rename map to the internal _passages structure:
    // removes renamed entries from per-passage groups and prepends a Common group.
    // Also updates _globalValueToKey so BuildCommentMap sees the new key names.
    public void ApplyRenames(Dictionary<string, string> renames)
    {
        if (renames.Count == 0) return;

        var commonEntries = new List<Entry>();

        foreach (var (_, entries) in _passages)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var e = entries[i];
                if (renames.TryGetValue(e.Key, out var newKey))
                {
                    commonEntries.Add(new Entry(newKey, e.Value));
                    entries.RemoveAt(i);
                    _globalValueToKey[e.Value] = newKey;
                }
            }
        }

        commonEntries.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));

        if (commonEntries.Count > 0)
            _passages.Insert(0, ("(Common)", commonEntries));
    }

    // Recursively replaces restext://OldKey with restext://NewKey in a V2Serializer dict.
    public static void ApplyRenamesInDict(Dictionary<string, object?> d, Dictionary<string, string> renames)
    {
        foreach (var k in d.Keys.ToArray())
        {
            switch (d[k])
            {
                case string s when s.StartsWith("restext://", StringComparison.Ordinal):
                    var oldKey = s[10..];
                    if (renames.TryGetValue(oldKey, out var newKey))
                        d[k] = $"restext://{newKey}";
                    break;
                case Dictionary<string, object?> sub:
                    ApplyRenamesInDict(sub, renames);
                    break;
                case IEnumerable<Dictionary<string, object?>> list:
                    foreach (var item in list)
                        ApplyRenamesInDict(item, renames);
                    break;
            }
        }
    }

    // Reports deduplication stats.
    public void ReportDeduplicationStats(ExtractionReport report)
    {
        int commonCount = _keyPassages.Count(kv => kv.Value.Count > 1);
        if (commonCount > 0)
            report.AddInfo("[restext]",
                $"{commonCount} Common string key(s) shared across passages; {_reuseCount} total reference(s) deduplicated.");
    }

    // ── Walking ────────────────────────────────────────────────────────────

    private void WalkNodeList(object? listObj)
    {
        foreach (var d in AsDictList(listObj))
            WalkNode(d);
    }

    private void WalkNode(Dictionary<string, object?> d)
    {
        if (!d.TryGetValue("type", out var typeObj) || typeObj is not string type) return;

        switch (type)
        {
            case "text":
                WalkTextNode(d);
                break;
            case "navigation":
                ExtractField(d, "label");
                break;
            case "popup":
                ExtractField(d, "label");
                break;
            case "input":
                ExtractField(d, "text");
                break;
            case "section":
                ExtractField(d, "title");
                break;
            case "assign":
            case "let":
                WalkExprNode(d);
                break;
        }

        // Recurse into all container children, regardless of type.
        WalkNodeList(d.GetValueOrDefault("nodes"));
        WalkBranchOrCaseList(d.GetValueOrDefault("branches"));
        WalkBranchOrCaseList(d.GetValueOrDefault("cases"));
    }

    private void WalkTextNode(Dictionary<string, object?> d)
    {
        // v0.2: value field (combined template + inline style applied by V2Serializer)
        if (d.TryGetValue("value", out var val) && val is string vs && !IsSingleVarRef(vs))
            d["value"] = AllocKey(vs);
    }

    private void WalkBranchOrCaseList(object? listObj)
    {
        foreach (var d in AsDictList(listObj))
            WalkNodeList(d.GetValueOrDefault("nodes"));
    }

    private void ExtractField(Dictionary<string, object?> d, string field)
    {
        if (d.TryGetValue(field, out var val) && val is string s && !string.IsNullOrEmpty(s) && !IsSingleVarRef(s))
            d[field] = AllocKey(s);
    }

    // Extracts string literals from assign/let expr fields.
    // Handles two patterns:
    //   1. Shuffled-array: ["str1", "str2", ...].shuffled("key")[0] — extracts each quoted string
    //   2. Template string: The {townname} {_rnd_0} — extracts the whole expr as a restext key
    private void WalkExprNode(Dictionary<string, object?> d)
    {
        if (!d.TryGetValue("expr", out var exprObj) || exprObj is not string expr) return;

        var m = ShuffledArrayPrefixRegex().Match(expr);
        if (m.Success)
        {
            var arrayPart = m.Groups[1].Value;
            var newArrayPart = QuotedStringInExprRegex().Replace(arrayPart, ms =>
            {
                var raw = ms.Groups[1].Value;
                var unescaped = raw.Replace("\\\"", "\"").Replace("\\\\", "\\");
                if (unescaped.StartsWith("restext://") || IsNumericString(unescaped) || IsSeedKeyLike(unescaped))
                    return ms.Value;
                return $"\"{AllocKey(unescaped)}\"";
            });

            if (newArrayPart != arrayPart)
            {
                int start = m.Groups[1].Index;
                d["expr"] = expr[..start] + newArrayPart + expr[(start + arrayPart.Length)..];
            }
            return;
        }

        // Template string: plain text with {var} interpolations, e.g. "The {townname} {_rnd_0}"
        if (IsTemplateExpr(expr))
            d["expr"] = AllocKey(expr);
    }

    private static bool IsTemplateExpr(string expr)
    {
        if (!expr.Contains('{')) return false;
        // Skip quoted strings, arrays, function calls, ternary expressions
        if (expr.StartsWith('"') || expr.StartsWith('[') || expr.StartsWith('(') || expr.StartsWith("?("))
            return false;
        // A bare single-var ref like {varname} has no static text — not a template
        if (IsSingleVarRef(expr)) return false;
        // Must have non-whitespace text outside {var} placeholders
        var stripped = PlaceholderRegex().Replace(expr, "");
        return !string.IsNullOrWhiteSpace(stripped);
    }

    // Matches {varname} or {icon:slug} placeholders
    [GeneratedRegex(@"\{[^{}]+\}")]
    private static partial Regex PlaceholderRegex();

    // Matches [...].shuffled( at the start — captures array contents in group 1.
    [GeneratedRegex(@"^\[(.+?)\]\.shuffled\(")]
    private static partial Regex ShuffledArrayPrefixRegex();

    // Matches a double-quoted string literal (handles \" escapes).
    [GeneratedRegex(@"""((?:[^""\\]|\\.)*)""")]
    private static partial Regex QuotedStringInExprRegex();

    // Seed key pattern: ends with _digits (e.g. "FearoftheUnknownStart_4"); not display text.
    private static bool IsSeedKeyLike(string s) =>
        !s.Contains(' ') && SeedKeySuffixRegex().IsMatch(s);

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex SeedKeySuffixRegex();

    // ── Key allocation ─────────────────────────────────────────────────────

    private string AllocKey(string value)
    {
        // Skip strings with no displayable content: whitespace/punctuation only (no letters,
        // digits, or {placeholder} refs). These are formatting artifacts, not localizable strings.
        if (!HasDisplayableContent(value)) return value;

        if (_globalValueToKey.TryGetValue(value, out var existing))
        {
            _reuseCount++;
            _keyPassages[existing].Add(_passageId);
            return $"restext://{existing}";
        }
        var key = $"{_passageId}_{_counter:D3}";
        _counter++;
        _current.Add(new Entry(key, value));
        _globalValueToKey[value] = key;
        _keyPassages[key] = [_passageId];
        return $"restext://{key}";
    }

    private static bool HasDisplayableContent(string s) =>
        s.Any(char.IsLetterOrDigit) || PlaceholderRegex().IsMatch(s);

    // ── Helpers ────────────────────────────────────────────────────────────

    // A single {var} reference with no other content — skip extraction.
    private static readonly Regex SingleVarRegex = SingleVarPattern();
    [GeneratedRegex(@"^\{[^{}]+\}$")]
    private static partial Regex SingleVarPattern();

    private static bool IsSingleVarRef(string s) => SingleVarRegex.IsMatch(s);

    private static bool IsNumericString(string s) => long.TryParse(s, out _);

    private static IEnumerable<Dictionary<string, object?>> AsDictList(object? obj)
    {
        if (obj is IEnumerable<Dictionary<string, object?>> typed)
            foreach (var d in typed) yield return d;
        else if (obj is System.Collections.IEnumerable items)
            foreach (var item in items)
                if (item is Dictionary<string, object?> d) yield return d;
    }
}
