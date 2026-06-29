using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Masterwork.Extractor;

/// <summary>
/// Walks a passage's ToDict() output, extracts human-readable strings into
/// restext entries (Key=Value format), and mutates the dict in-place to replace
/// those strings with restext://Key references.
/// </summary>
public sealed partial class RestextCollector
{
    public sealed record Entry(string Key, string Value);

    private readonly List<(string FileName, List<Entry> Entries)> _passages = [];
    private List<Entry> _current = [];
    private string _passageId = "";
    private int _counter;

    // Called once per passage before serialization.
    // Transforms the ToDict() output in-place; accumulates entries for this passage.
    public void CollectPassage(string passageId, string fileName, Dictionary<string, object?> passageDict)
    {
        _passageId = passageId;
        _counter = 1;
        _current = [];

        if (passageDict.TryGetValue("nodes", out var nodes))
            WalkNodeList(nodes);

        if (_current.Count > 0)
            _passages.Add((fileName, _current));
    }

    // Returns all entries grouped by passage, in passage order.
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
            case "section_heading":
                ExtractField(d, "text");
                break;
            case "link":
                ExtractField(d, "label");
                break;
            case "expand_link":
                ExtractField(d, "label");
                WalkNodeList(d.GetValueOrDefault("expand_nodes"));
                break;
            case "input_prompt":
                ExtractField(d, "text");
                break;
            case "setup_notification":
                ExtractField(d, "title");
                ExtractField(d, "text");
                break;
            case "set_location":
                ExtractField(d, "name");
                break;
            case "let":
                WalkVarRandom(d.GetValueOrDefault("random"));
                break;
            case "effect":
                WalkEffectVarRandom(d);
                break;
        }

        // Recurse into all container children, regardless of type.
        WalkNodeList(d.GetValueOrDefault("nodes"));
        WalkBranchOrCaseList(d.GetValueOrDefault("branches"));
        WalkBranchOrCaseList(d.GetValueOrDefault("cases"));
    }

    private void WalkTextNode(Dictionary<string, object?> d)
    {
        // Template form: extract if not solely a single {var} placeholder.
        if (d.TryGetValue("template", out var tmpl) && tmpl is string ts && !IsSingleVarRef(ts))
            d["template"] = AllocKey(ts);

        // Runs form: extract text content of each run.
        if (d.TryGetValue("runs", out var runs))
            foreach (var run in AsDictList(runs))
                ExtractField(run, "text");
    }

    private void WalkVarRandom(object? rndObj)
    {
        if (rndObj is not Dictionary<string, object?> rnd) return;
        if (!rnd.TryGetValue("values", out var vals) || vals is not List<object> values) return;

        var replaced = new List<object>(values.Count);
        bool changed = false;
        foreach (var v in values)
        {
            if (v is string s && !IsSingleVarRef(s) && !IsNumericString(s))
            {
                replaced.Add(AllocKey(s));
                changed = true;
            }
            else
                replaced.Add(v);
        }
        if (changed) rnd["values"] = replaced;
    }

    private void WalkEffectVarRandom(Dictionary<string, object?> d)
    {
        if (!d.TryGetValue("var_random", out var vr) || vr is not Dictionary<string, object?> vrDict) return;
        foreach (var entry in vrDict)
            WalkVarRandom(entry.Value);
    }

    private void WalkBranchOrCaseList(object? listObj)
    {
        foreach (var d in AsDictList(listObj))
            WalkNodeList(d.GetValueOrDefault("nodes"));
    }

    private void ExtractField(Dictionary<string, object?> d, string field)
    {
        if (d.TryGetValue(field, out var val) && val is string s && !string.IsNullOrEmpty(s))
            d[field] = AllocKey(s);
    }

    // ── Key allocation ─────────────────────────────────────────────────────

    private string AllocKey(string value)
    {
        var key = $"{_passageId}_{_counter:D3}";
        _counter++;
        _current.Add(new Entry(key, value));
        return $"restext://{key}";
    }

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
