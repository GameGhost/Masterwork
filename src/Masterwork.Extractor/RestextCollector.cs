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
///     and their entries moved to a "(Common)" group prepended to Passages. A caller-supplied
///     curated ID (see the constructor's curatedRestext parameter) takes priority over the
///     auto-generated Common_{NNN} name whenever its value matches — this is the one place IDs are
///     otherwise unstable across re-extractions, since the counter's assignment order depends on
///     passage traversal order. ReportUnusedCuratedIds flags any curated entry that never matched.
/// </summary>
public sealed partial class RestextCollector
{
    public sealed record Entry(string Key, string Value);

    private readonly List<(string FileName, List<Entry> Entries)> _passages = [];
    private readonly HashSet<string> _nonTemplateFileNames = new(StringComparer.Ordinal);
    private List<Entry> _current = [];
    private readonly Dictionary<string, string> _globalValueToKey = [];  // value → key, global dedup
    private readonly Dictionary<string, HashSet<string>> _keyPassages = [];  // key → passage IDs that use it
    private readonly HashSet<string> _passageIds;
    private int _reuseCount;
    private int _commonCounter = 1;
    private string _passageId = "";
    private int _counter;

    // Manually curated Common IDs (value → key), keyed by the exact display text. When a string is
    // promoted to a Common key (used in 2+ passages — see BuildRenameMap), a matching curated ID is
    // used instead of the next auto-generated Common_NNN, so override/manually-written passages can
    // reference a name that doesn't shift on every re-extraction.
    private readonly IReadOnlyDictionary<string, string> _curatedKeyToValue;
    private readonly Dictionary<string, string> _curatedValueToKey;
    private readonly HashSet<string> _curatedUsedKeys = new(StringComparer.Ordinal);

    // Tracks keys newly created from assign/let node extractions (not display-text fields).
    // Used by RestoreNonTemplateAssignments to prune entries whose variable is never used in templates.
    private sealed class AssignedKeyRecord(string value)
    {
        public string Value { get; } = value;
        public HashSet<string> VarNames { get; } = [];
        // dict["expr"] IS exactly '"restext://KEY"' — restore by full field replacement.
        public List<Dictionary<string, object?>> DirectDicts { get; } = [];
        // dict["expr"] CONTAINS the key inside a larger expression (e.g. shuffled array).
        // Restore by string substitution within the existing field value.
        public List<Dictionary<string, object?>> EmbeddedDicts { get; } = [];
    }
    private readonly Dictionary<string, AssignedKeyRecord> _newAssignmentKeys = [];

    public RestextCollector(IEnumerable<string>? passageIds = null, IReadOnlyDictionary<string, string>? curatedRestext = null)
    {
        _passageIds = passageIds is null ? [] : new HashSet<string>(passageIds, StringComparer.Ordinal);
        _curatedKeyToValue = curatedRestext ?? new Dictionary<string, string>();
        _curatedValueToKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in _curatedKeyToValue)
        {
            // Last one wins on a duplicate value — the losing key is then reported unused, which
            // correctly flags the ambiguity for the curated file's author to resolve.
            _curatedValueToKey[value] = key;
        }
    }

    // Called once per passage before serialization.
    // Transforms the V2Serializer.ToDict() output in-place; accumulates entries for this passage.
    // isTemplate=false marks the passage as non-display (e.g. tagged notext); its strings are
    // still extracted so they appear in restext, but they are excluded from BuildTemplateVarNames
    // so {varName} patterns in documentation text don't prevent RestoreNonTemplateAssignments.
    public void CollectPassage(string passageId, string fileName, Dictionary<string, object?> passageDict, bool isTemplate = true)
    {
        _passageId = SanitizeForRestextKey(passageId);
        _counter = 1;
        _current = [];

        // Passage-level headers, not nodes.
        ExtractField(passageDict, "title");
        ExtractField(passageDict, "subtitle");
        if (passageDict.TryGetValue("location", out var locObj) && locObj is Dictionary<string, object?> loc)
        {
            ExtractField(loc, "name");
        }

        if (passageDict.TryGetValue("nodes", out var nodes))
        {
            WalkNodeList(nodes);
        }

        if (_current.Count > 0)
        {
            _passages.Add((fileName, _current));
            if (!isTemplate)
            {
                _nonTemplateFileNames.Add(fileName);
            }
        }
    }

    // Returns all entries grouped by passage, in passage order.
    // After ApplyRenames: index 0 is the Common group (if any), followed by per-passage groups.
    public IReadOnlyList<(string FileName, List<Entry> Entries)> Passages => _passages;

    // Build a lookup map Key → Value for comment injection.
    public Dictionary<string, string> BuildCommentMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, entries) in _passages)
        {
            foreach (var e in entries)
            {
                map[e.Key] = e.Value;
            }
        }

        return map;
    }

    // ── Phase 2: rename multi-passage keys to Common_NNN ──────────────────

    // Returns a map of old key → Common_NNN (or a matching curated ID) for every key referenced in
    // 2+ passages. Call after all CollectPassage calls are done.
    public Dictionary<string, string> BuildRenameMap()
    {
        var keyToValue = BuildCommentMap();
        var renames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, passages) in _keyPassages.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (passages.Count <= 1)
            {
                continue;
            }

            if (_curatedValueToKey.TryGetValue(keyToValue[key], out var curatedKey))
            {
                renames[key] = curatedKey;
                _curatedUsedKeys.Add(curatedKey);
            }
            else
            {
                renames[key] = $"Common_{_commonCounter:D3}";
                _commonCounter++;
            }
        }
        return renames;
    }

    // Logs a warning for every curated ID that never matched any extracted string this run — these
    // are silently dropped from the output restext file (only entries actually produced by
    // extraction are written), so this is the only place that surfaces the gap.
    public void ReportUnusedCuratedIds(ExtractionReport report)
    {
        foreach (var (key, value) in _curatedKeyToValue.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!_curatedUsedKeys.Contains(key))
            {
                report.AddWarning("[restext]",
                    $"Curated restext ID '{key}' (\"{value}\") was not used during extraction — omitted from en-US.restext.");
            }
        }
    }

    // Applies the rename map to the internal _passages structure:
    // removes renamed entries from per-passage groups and prepends a Common group.
    // Also updates _globalValueToKey so BuildCommentMap sees the new key names.
    public void ApplyRenames(Dictionary<string, string> renames)
    {
        if (renames.Count == 0)
        {
            return;
        }

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
        {
            _passages.Insert(0, ("(Common)", commonEntries));
        }
    }

    // Recursively replaces restext://OldKey with restext://NewKey in a V2Serializer dict.
    // Handles both direct restext values ("restext://Key") and expression strings that
    // contain embedded restext URIs (e.g. shuffled-array expr fields).
    public static void ApplyRenamesInDict(Dictionary<string, object?> d, Dictionary<string, string> renames)
    {
        foreach (var k in d.Keys.ToArray())
        {
            switch (d[k])
            {
                case string s when s.StartsWith("restext://", StringComparison.Ordinal):
                    var oldKey = s[10..];
                    if (renames.TryGetValue(oldKey, out var newKey))
                    {
                        d[k] = $"restext://{newKey}";
                    }

                    break;
                case string s when s.Contains("restext://", StringComparison.Ordinal):
                    d[k] = RestextKeyInStringRegex().Replace(s, m =>
                    {
                        var oldEmbedKey = m.Groups[1].Value;
                        return renames.TryGetValue(oldEmbedKey, out var newEmbedKey)
                            ? $"restext://{newEmbedKey}"
                            : m.Value;
                    });
                    break;
                case Dictionary<string, object?> sub:
                    ApplyRenamesInDict(sub, renames);
                    break;
                case IEnumerable<Dictionary<string, object?>> list:
                    foreach (var item in list)
                    {
                        ApplyRenamesInDict(item, renames);
                    }

                    break;
            }
        }
    }

    // Scans condition/match string literals across passages and registers cross-passage usage
    // so those values get promoted to Common_NNN keys.
    // Call BEFORE BuildRenameMap so promotions are included in the rename pass.
    public void ScanConditionLiterals(IEnumerable<(string PassageId, Dictionary<string, object?> Dict)> passages)
    {
        foreach (var (passageId, dict) in passages)
        {
            _passageId = SanitizeForRestextKey(passageId);
            ScanConditionLiteralsInNodeList(dict.GetValueOrDefault("nodes"));
        }
    }

    // Replaces string literals in condition/match fields with restext://Key URIs when the literal
    // exactly matches a known restext value.
    // Call AFTER ApplyRenames so final Common_NNN keys are used.
    public void ApplyConditionLiteralReplacements(IEnumerable<Dictionary<string, object?>> dicts)
    {
        foreach (var dict in dicts)
        {
            ApplyConditionReplacementsInNodeList(dict.GetValueOrDefault("nodes"));
        }
    }

    // After all CollectPassage calls: restores string literals that were provisionally extracted
    // to restext from assign/let expr fields but whose variable names never appear as {varName}
    // placeholders in any display-text template.  Variables that ARE used in templates stay as
    // restext:// refs; the rest revert to raw string literals.
    // Call BEFORE ScanConditionLiterals so removed keys don't influence the rename pass.
    public void RestoreNonTemplateAssignments()
    {
        if (_newAssignmentKeys.Count == 0)
        {
            return;
        }

        var templateVarNames = BuildTemplateVarNames();
        // Known popup layout display-text properties are always treated as template-used.
        templateVarNames.UnionWith(["title", "bodyText", "message", "instruction"]);

        foreach (var (key, rec) in _newAssignmentKeys)
        {
            if (rec.VarNames.All(v => !templateVarNames.Contains(v)))
            {
                // Restore direct assignments: whole expr was '"restext://KEY"'
                var restored = $"\"{rec.Value}\"";
                foreach (var dict in rec.DirectDicts)
                {
                    dict["expr"] = restored;
                }

                // Restore embedded occurrences (shuffled arrays) via string substitution.
                // The value must be re-escaped to match the array element syntax.
                if (rec.EmbeddedDicts.Count > 0)
                {
                    var escapedInArray = rec.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    foreach (var dict in rec.EmbeddedDicts)
                    {
                        if (dict.TryGetValue("expr", out var exprObj) && exprObj is string exprStr)
                        {
                            dict["expr"] = exprStr.Replace($"restext://{key}", escapedInArray);
                        }
                    }
                }

                _globalValueToKey.Remove(rec.Value);
                _keyPassages.Remove(key);
                foreach (var (_, entries) in _passages)
                {
                    entries.RemoveAll(e => e.Key == key);
                }
            }
        }
    }

    // Scans all extracted restext values for {varName} placeholders and returns the set of
    // variable names that appear in display-text templates.
    // Skips non-template passages (e.g. notext developer docs) to avoid false positives.
    private HashSet<string> BuildTemplateVarNames()
    {
        var varNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (fileName, entries) in _passages)
        {
            if (_nonTemplateFileNames.Contains(fileName))
            {
                continue;
            }

            foreach (var entry in entries)
            {
                foreach (Match m in PlaceholderRegex().Matches(entry.Value))
                {
                    var content = m.Value[1..^1]; // strip { }
                    if (content.StartsWith("icon:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var split = content.IndexOfAny(['.', '[']);
                    varNames.Add(split >= 0 ? content[..split] : content);
                }
            }
        }
        return varNames;
    }

    private void ScanConditionLiteralsInNodeList(object? listObj)
    {
        foreach (var d in AsDictList(listObj))
        {
            ScanConditionLiteralsInNode(d);
        }
    }

    private void ScanConditionLiteralsInNode(Dictionary<string, object?> d)
    {
        if (!d.TryGetValue("type", out var typeObj) || typeObj is not string type)
        {
            return;
        }

        if (type == "conditional")
        {
            // Flat format: single if/then directly on the node
            if (d.TryGetValue("if", out var flatCondObj) && flatCondObj is string flatCond)
            {
                RegisterStringLiteralsInExpr(flatCond);
            }

            ScanConditionLiteralsInNodeList(d.GetValueOrDefault("then"));
            // Multi-branch format: conditions array
            foreach (var branch in AsDictList(d.GetValueOrDefault("conditions")))
            {
                if (branch.TryGetValue("if", out var condObj) && condObj is string cond)
                {
                    RegisterStringLiteralsInExpr(cond);
                }

                ScanConditionLiteralsInNodeList(branch.GetValueOrDefault("then"));
            }
            ScanConditionLiteralsInNodeList(d.GetValueOrDefault("else"));
        }
        else if (type == "switch")
        {
            foreach (var c in AsDictList(d.GetValueOrDefault("cases")))
            {
                if (c.TryGetValue("match", out var matchObj) && matchObj is string matchStr)
                {
                    TryRegisterConditionLiteral(matchStr);
                }

                ScanConditionLiteralsInNodeList(c.GetValueOrDefault("nodes"));
            }
            ScanConditionLiteralsInNodeList(d.GetValueOrDefault("default"));
        }
        else if (type is "assign" or "let")
        {
            if (d.TryGetValue("expr", out var exprObj) && exprObj is string expr)
            {
                RegisterStringLiteralsInExpr(expr);
            }
        }
        else
        {
            TryScanStringField(d, "label");
            TryScanStringField(d, "value");
            TryScanStringField(d, "title");
            TryScanStringField(d, "text");
            TryScanStringField(d, "snapshot"); // link/popup's own field — only a string when it's a snapshot label, harmlessly skipped when it's a bool
            TryScanStringField(d, "snapshot_label"); // goto's own field
            TryScanStringField(d, "display");
            TryScanStringField(d, "name");
            TryScanStringField(d, "okay");
            TryScanStringField(d, "cancel");
        }

        ScanConditionLiteralsInNodeList(d.GetValueOrDefault("content"));
        ScanConditionLiteralsInNodeList(d.GetValueOrDefault("onclick"));
        ScanConditionLiteralsInNodeList(d.GetValueOrDefault("onclose"));
        ScanConditionLiteralsInNodeList(d.GetValueOrDefault("do"));
        ScanConditionLiteralsInNodeList(d.GetValueOrDefault("nodes")); // switch case bodies
    }

    private void RegisterStringLiteralsInExpr(string expr)
    {
        foreach (Match m in QuotedStringInExprRegex().Matches(expr))
        {
            var inner = m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            TryRegisterConditionLiteral(inner);
        }
    }

    private void TryRegisterConditionLiteral(string value)
    {
        if (_globalValueToKey.TryGetValue(value, out var key) && _keyPassages.ContainsKey(key))
        {
            _keyPassages[key].Add(_passageId);
        }
    }

    private void TryScanStringField(Dictionary<string, object?> d, string field)
    {
        if (d.TryGetValue(field, out var val) && val is string s)
        {
            TryRegisterConditionLiteral(s);
        }
    }

    private void ApplyConditionReplacementsInNodeList(object? listObj)
    {
        foreach (var d in AsDictList(listObj))
        {
            ApplyConditionReplacementsInNode(d);
        }
    }

    private void ApplyConditionReplacementsInNode(Dictionary<string, object?> d)
    {
        if (!d.TryGetValue("type", out var typeObj) || typeObj is not string type)
        {
            return;
        }

        if (type == "conditional")
        {
            // Flat format: single if/then directly on the node
            if (d.TryGetValue("if", out var flatCondObj) && flatCondObj is string flatCond)
            {
                d["if"] = ReplaceStringLiteralsInCondExpr(flatCond);
            }

            ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("then"));
            // Multi-branch format: conditions array
            foreach (var branch in AsDictList(d.GetValueOrDefault("conditions")))
            {
                if (branch.TryGetValue("if", out var condObj) && condObj is string cond)
                {
                    branch["if"] = ReplaceStringLiteralsInCondExpr(cond);
                }

                ApplyConditionReplacementsInNodeList(branch.GetValueOrDefault("then"));
            }
            ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("else"));
        }
        else if (type == "switch")
        {
            foreach (var c in AsDictList(d.GetValueOrDefault("cases")))
            {
                if (c.TryGetValue("match", out var matchObj) && matchObj is string matchStr &&
                    !matchStr.StartsWith("restext://", StringComparison.Ordinal) &&
                    _globalValueToKey.TryGetValue(matchStr, out var key))
                {
                    c["match"] = $"restext://{key}";
                }
                ApplyConditionReplacementsInNodeList(c.GetValueOrDefault("nodes"));
            }
            ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("default"));
        }
        // assign/let exprs are NOT processed here: Phase 1 (WalkExprNode) extracts them, and
        // RestoreNonTemplateAssignments prunes non-template assignments back to raw literals.
        // Re-applying replacement here would undo those restorations.
        else
        {
            // Safety net for all other node types: replace any raw string in a display-text
            // field that exactly matches a restext value. Fields already extracted in Phase 1
            // are already restext:// refs and are skipped by TryReplaceStringField.
            TryReplaceStringField(d, "label");
            TryReplaceStringField(d, "value");
            TryReplaceStringField(d, "title");
            TryReplaceStringField(d, "text");
            TryReplaceStringField(d, "snapshot"); // link/popup's own field — only a string when it's a snapshot label, harmlessly skipped when it's a bool
            TryReplaceStringField(d, "snapshot_label"); // goto's own field
            TryReplaceStringField(d, "display");
            TryReplaceStringField(d, "name");
            TryReplaceStringField(d, "okay");
            TryReplaceStringField(d, "cancel");
        }

        ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("content"));
        ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("onclick"));
        ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("onclose"));
        ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("do"));
        ApplyConditionReplacementsInNodeList(d.GetValueOrDefault("nodes")); // switch case bodies
    }

    private void TryReplaceStringField(Dictionary<string, object?> d, string field)
    {
        if (d.TryGetValue(field, out var val) && val is string s &&
            !s.StartsWith("restext://", StringComparison.Ordinal) &&
            _globalValueToKey.TryGetValue(s, out var key))
        {
            d[field] = $"restext://{key}";
        }
    }

    private string ReplaceStringLiteralsInCondExpr(string expr)
    {
        return QuotedStringInExprRegex().Replace(expr, m =>
        {
            var raw = m.Groups[1].Value;
            var unescaped = raw.Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (unescaped.StartsWith("restext://", StringComparison.Ordinal))
            {
                return m.Value;
            }

            if (_globalValueToKey.TryGetValue(unescaped, out var key))
            {
                return $"\"restext://{key}\"";
            }

            return m.Value;
        });
    }

    // Reports deduplication stats.
    public void ReportDeduplicationStats(ExtractionReport report)
    {
        int commonCount = _keyPassages.Count(kv => kv.Value.Count > 1);
        if (commonCount > 0)
        {
            report.AddInfo("[restext]",
                $"{commonCount} Common string key(s) shared across passages; {_reuseCount} total reference(s) deduplicated.");
        }
    }

    // ── Walking ────────────────────────────────────────────────────────────

    private void WalkNodeList(object? listObj)
    {
        foreach (var d in AsDictList(listObj))
        {
            WalkNode(d);
        }
    }

    private void WalkNode(Dictionary<string, object?> d)
    {
        if (!d.TryGetValue("type", out var typeObj) || typeObj is not string type)
        {
            return;
        }

        switch (type)
        {
            case "text":
                WalkTextNode(d);
                break;
            case "link":
                ExtractField(d, "label");
                break;
            case "popup":
                ExtractField(d, "label");
                ExtractField(d, "okay");
                ExtractField(d, "cancel");
                break;
            case "input":
                ExtractField(d, "label");
                break;
            case "image":
                ExtractField(d, "title");
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
        WalkNodeList(d.GetValueOrDefault("content"));                       // popup, section
        WalkNodeList(d.GetValueOrDefault("onclick"));                       // link
        WalkNodeList(d.GetValueOrDefault("onclose"));                       // popup
        WalkNodeList(d.GetValueOrDefault("do"));                            // foreach
        WalkNodeList(d.GetValueOrDefault("else"));                          // conditional
        WalkNodeList(d.GetValueOrDefault("default"));                       // switch
        WalkNodeList(d.GetValueOrDefault("then"));                          // conditional flat
        WalkBranchOrCaseList(d.GetValueOrDefault("conditions"), "then");    // conditional multi
        WalkBranchOrCaseList(d.GetValueOrDefault("cases"), "nodes");        // switch cases
    }

    private void WalkTextNode(Dictionary<string, object?> d)
    {
        // v0.2: value field (combined template + inline style applied by V2Serializer)
        if (d.TryGetValue("value", out var val) && val is string vs && !IsSingleVarRef(vs))
        {
            d["value"] = AllocKey(vs);
        }
    }

    private void WalkBranchOrCaseList(object? listObj, string bodyField = "nodes")
    {
        foreach (var d in AsDictList(listObj))
        {
            WalkNodeList(d.GetValueOrDefault(bodyField));
        }
    }

    private void ExtractField(Dictionary<string, object?> d, string field)
    {
        if (d.TryGetValue(field, out var val) && val is string s && !string.IsNullOrEmpty(s) && !IsSingleVarRef(s))
        {
            d[field] = AllocKey(s);
        }
    }

    // Extracts string literals from assign/let expr fields.
    // Handles three patterns:
    //   1. Quoted string literal in a text-content let property (title, bodyText, etc.)
    //   2. Shuffled-array: ["str1", "str2", ...].shuffled("key")[0] — extracts each quoted string
    //   3. Template string: The {townname} {_rnd_0} — extracts the whole expr as a restext key
    private void WalkExprNode(Dictionary<string, object?> d)
    {
        if (!d.TryGetValue("expr", out var exprObj) || exprObj is not string expr)
        {
            return;
        }

        // Quoted string literal assignment: extract all to restext; non-template var assignments
        // are pruned back to raw literals by RestoreNonTemplateAssignments after all passages are collected.
        if (expr.StartsWith('"') && expr.EndsWith('"') && expr.Length >= 4)
        {
            var inner = expr[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
            if (!inner.StartsWith("restext://", StringComparison.Ordinal) &&
                !IsNumericString(inner) && !_passageIds.Contains(inner))
            {
                d.TryGetValue("var", out var varObj);
                var varName = varObj as string ?? "";
                bool wasNew = !_globalValueToKey.ContainsKey(inner);
                var keyRef = AllocKey(inner);
                if (keyRef.StartsWith("restext://", StringComparison.Ordinal))
                {
                    d["expr"] = $"\"{keyRef}\"";
                    var key = keyRef[10..];
                    if (wasNew)
                    {
                        var rec = new AssignedKeyRecord(inner);
                        rec.VarNames.Add(varName);
                        rec.DirectDicts.Add(d);
                        _newAssignmentKeys[key] = rec;
                    }
                    else if (_newAssignmentKeys.TryGetValue(key, out var rec))
                    {
                        rec.VarNames.Add(varName);
                        rec.DirectDicts.Add(d);
                    }
                }
            }
            return;
        }

        var m = ShuffledArrayPrefixRegex().Match(expr);
        if (m.Success)
        {
            d.TryGetValue("var", out var saVarObj);
            var saVarName = saVarObj as string ?? "";
            var newKeys = new List<string>(); // keys extracted from this array's elements

            var arrayPart = m.Groups[1].Value;
            var newArrayPart = QuotedStringInExprRegex().Replace(arrayPart, ms =>
            {
                var raw = ms.Groups[1].Value;
                var unescaped = raw.Replace("\\\"", "\"").Replace("\\\\", "\\");
                if (unescaped.StartsWith("restext://") || IsNumericString(unescaped) || IsSeedKeyLike(unescaped) || _passageIds.Contains(unescaped))
                {
                    return ms.Value;
                }

                bool wasNew = !_globalValueToKey.ContainsKey(unescaped);
                var keyRef = AllocKey(unescaped);
                if (keyRef.StartsWith("restext://", StringComparison.Ordinal))
                {
                    var key = keyRef[10..];
                    newKeys.Add(key);
                    if (wasNew)
                    {
                        var rec = new AssignedKeyRecord(unescaped);
                        rec.VarNames.Add(saVarName);
                        _newAssignmentKeys[key] = rec;
                    }
                    else if (_newAssignmentKeys.TryGetValue(key, out var rec))
                    {
                        rec.VarNames.Add(saVarName);
                    }
                }
                return keyRef.StartsWith("restext://", StringComparison.Ordinal) ? $"\"{keyRef}\"" : ms.Value;
            });

            // Register this dict for potential embedded restoration
            foreach (var key in newKeys)
            {
                if (_newAssignmentKeys.TryGetValue(key, out var rec) && !rec.EmbeddedDicts.Contains(d))
                {
                    rec.EmbeddedDicts.Add(d);
                }
            }

            if (newArrayPart != arrayPart)
            {
                int start = m.Groups[1].Index;
                d["expr"] = expr[..start] + newArrayPart + expr[(start + arrayPart.Length)..];
            }
            return;
        }

        // Template string: plain text with {var} interpolations, e.g. "The {townname} {_rnd_0}"
        if (IsTemplateExpr(expr))
        {
            d["expr"] = AllocKey(expr);
        }
    }

    private static bool IsTemplateExpr(string expr)
    {
        if (!expr.Contains('{'))
        {
            return false;
        }
        // Skip quoted strings, arrays, function calls, ternary expressions
        if (expr.StartsWith('"') || expr.StartsWith('[') || expr.StartsWith('(') || expr.StartsWith("?("))
        {
            return false;
        }
        // A bare single-var ref like {varname} has no static text — not a template
        if (IsSingleVarRef(expr))
        {
            return false;
        }
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

    // Matches restext://Key inside a longer string (e.g. within an expr field).
    [GeneratedRegex(@"restext://([A-Za-z0-9_]+)")]
    private static partial Regex RestextKeyInStringRegex();

    // ── Key allocation ─────────────────────────────────────────────────────

    private string AllocKey(string value)
    {
        // Skip strings with no displayable content: whitespace/punctuation only (no letters,
        // digits, or {placeholder} refs). These are formatting artifacts, not localizable strings.
        if (!HasDisplayableContent(value))
        {
            return value;
        }

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

    // Restext keys must match [a-zA-Z_][a-zA-Z0-9_]* (RestextResolver's restext://Key regex
    // requires exactly this shape). Passage IDs themselves are left untouched everywhere else —
    // navigation targets, file names, etc. all still use the raw Cradle-derived ID, spaces/hyphens
    // and all — this sanitization applies only to the passage-ID prefix used when building a
    // restext key, so a key derived from e.g. "TCOD-TownName" or "TITLE SCREEN" is well-formed.
    private static string SanitizeForRestextKey(string passageId)
    {
        var sanitized = InvalidKeyCharRegex().Replace(passageId, "_");
        return sanitized.Length > 0 && (char.IsAsciiLetter(sanitized[0]) || sanitized[0] == '_')
            ? sanitized
            : "_" + sanitized;
    }

    [GeneratedRegex(@"[^A-Za-z0-9_]")]
    private static partial Regex InvalidKeyCharRegex();

    // A single {var} reference with no other content — skip extraction.
    private static readonly Regex SingleVarRegex = SingleVarPattern();
    [GeneratedRegex(@"^\{[^{}]+\}$")]
    private static partial Regex SingleVarPattern();

    private static bool IsSingleVarRef(string s) => SingleVarRegex.IsMatch(s);

    private static bool IsNumericString(string s) => long.TryParse(s, out _);

    private static IEnumerable<Dictionary<string, object?>> AsDictList(object? obj)
    {
        if (obj is IEnumerable<Dictionary<string, object?>> typed)
        {
            foreach (var d in typed)
            {
                yield return d;
            }
        }
        else if (obj is System.Collections.IEnumerable items)
        {
            foreach (var item in items)
            {
                if (item is Dictionary<string, object?> d)
                {
                    yield return d;
                }
            }
        }
    }
}
