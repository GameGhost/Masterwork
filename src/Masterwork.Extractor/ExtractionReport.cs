using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.Extractor;

public class ExtractionReport
{
    private readonly record struct Flag(
        string PassageName,
        string Kind,
        string Detail,
        string? Code = null,
        int? SourceLine = null);

    private readonly record struct InputPromptRecord(
        string PassageName,
        string StoreIn,
        string InputType,
        string Text,
        int? SourceLine);

    private readonly List<Flag> _flags = [];
    private readonly List<InputPromptRecord> _inputPrompts = [];
    private Dictionary<string, VarDef>? _variables;

    public int PassagesExtracted { get; set; }
    public int VariablesDiscovered { get; set; }
    public int UnknownNodeCount => _flags.Count(f => f.Kind == "unknown_node" &&
        !_overrides.Any(o => o.PassageId == f.PassageName));

    // Set before Write() so the report can generate relative-path links.
    public string? SourceFilePath { get; set; }
    public string? OutputDirPath { get; set; }
    // Human-readable module name shown in the report header.
    public string? ModuleTitle { get; set; }

    // passage name → output filename (just the filename, report is in the same dir)
    public Dictionary<string, string> PassageFiles { get; } = new(StringComparer.Ordinal);

    // Tags reported in the Structure Tags section, in display order.
    public static readonly string[] StructureTags = ["Begins-Here", "INTRO", "HUB", "END"];

    // passage name → subset of StructureTags present on that passage
    private readonly Dictionary<string, string[]> _taggedPassages = new(StringComparer.Ordinal);

    public void AddTaggedPassage(string passageId, string[] tags)
    {
        var relevant = tags.Where(t => StructureTags.Contains(t, StringComparer.Ordinal)).ToArray();
        if (relevant.Length > 0)
            _taggedPassages[passageId] = relevant;
    }

    private List<string>? _isolatedPassages;
    public void AddIsolatedPassages(List<string> names) => _isolatedPassages = names;

    // Command-line settings recorded for the report. Populated by the caller before Write().
    public Dictionary<string, string> Settings { get; } = new(StringComparer.Ordinal);

    private readonly List<string> _restextExclusions = [];
    public void AddRestextExclusion(string passageId) => _restextExclusions.Add(passageId);

    private readonly List<(string PassageId, string FileName, int GeneratedUnknownCount)> _overrides = [];
    public void AddOverrideApplied(string passageId, string fileName)
    {
        var unknownCount = _flags.Count(f => f.Kind == "unknown_node" && f.PassageName == passageId);
        _overrides.Add((passageId, fileName, unknownCount));
    }

    // Unknown node — the code is the primary content; no separate message.
    public void AddUnhandled(string passageName, string code, int? sourceLine = null) =>
        _flags.Add(new(passageName, "unknown_node",
            code.Length > 600 ? code[..600] + "…" : code,
            null, sourceLine));

    // Warning — message describes the problem; optional code excerpt provides context.
    public void AddWarning(string passageName, string message, string? code = null, int? sourceLine = null) =>
        _flags.Add(new(passageName, "warning", message,
            code is null ? null : code.Length > 600 ? code[..600] + "…" : code,
            sourceLine));

    public void AddInfo(string passageName, string message, int? sourceLine = null) =>
        _flags.Add(new(passageName, "info", message, null, sourceLine));

    public void AddInputPrompt(string passageName, string storeIn, string inputType,
        string text, int? sourceLine = null) =>
        _inputPrompts.Add(new(passageName, storeIn, inputType, text, sourceLine));

    public void SetVariables(Dictionary<string, VarDef> vars)
    {
        _variables = vars;
        foreach (var p in _inputPrompts)
        {
            if (!_variables.TryGetValue(p.StoreIn, out var def)) continue;
            var expectedType = p.InputType == "number" ? "int" : "string";
            if (def.VarType != expectedType)
                AddWarning(p.PassageName,
                    $"Input type mismatch: `{p.StoreIn}` is declared `{def.VarType}` but input_type is `{p.InputType}`",
                    null, p.SourceLine);
        }
    }

    public void Write(string outputPath)
    {
        var sb = new StringBuilder();
        var title = ModuleTitle is { Length: > 0 } t ? $"Extraction Report — {t}" : "Extraction Report";
        sb.AppendLine($"# {title}");
        sb.AppendLine();

        // Flags for overridden passages reflect the discarded generated file — suppress them.
        var overriddenIds = new HashSet<string>(_overrides.Select(o => o.PassageId), StringComparer.Ordinal);
        var activeFlags = _flags.Where(f => !overriddenIds.Contains(f.PassageName)).ToList();

        var unknownCount = activeFlags.Count(f => f.Kind == "unknown_node");
        var warnCount = activeFlags.Count(f => f.Kind == "warning");
        var infoCount = activeFlags.Count(f => f.Kind == "info");
        var promptCount = _inputPrompts.Count;
        var isolatedCount = _isolatedPassages?.Count ?? 0;
        var overrideCount = _overrides.Count;

        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Passages extracted | **{PassagesExtracted}** |");
        sb.AppendLine($"| Variables discovered | **{VariablesDiscovered}** |");
        if (_restextExclusions.Count > 0)
            sb.AppendLine($"| Restext excluded | **{_restextExclusions.Count}** passages |");
        sb.AppendLine($"| Unknown nodes | **{unknownCount}** |");
        sb.AppendLine($"| Warnings | **{warnCount}** |");
        if (promptCount > 0)
            sb.AppendLine($"| Input prompts | **{promptCount}** |");
        if (infoCount > 0)
            sb.AppendLine($"| Info | **{infoCount}** |");
        if (isolatedCount > 0)
            sb.AppendLine($"| Isolated passages | **{isolatedCount}** |");
        if (overrideCount > 0)
            sb.AppendLine($"| Overrides applied | **{overrideCount}** |");
        sb.AppendLine();

        WriteSettingsSection(sb);
        WriteTaggedPassagesSection(sb);
        WriteOverridesSection(sb);
        WriteRestextExclusionsSection(sb);
        WriteSection(sb, activeFlags, "unknown_node", "Unknown Nodes",
            "Unrecognized statements — emitted as `type: unknown` in the passage YAML. Each requires manual review.");
        WriteSection(sb, activeFlags, "warning", "Warnings",
            "Recognized patterns that required a fallback or approximation.");
        WriteInputPromptsSection(sb);
        if (infoCount > 0)
            WriteSection(sb, activeFlags, "info", "Info",
                "Informational notes — no action required.");
        WriteIsolatedSection(sb);

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"Report written to: {outputPath}");
    }

    private void WriteSettingsSection(StringBuilder sb)
    {
        if (Settings.Count == 0) return;

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Settings");
        sb.AppendLine();
        sb.AppendLine("| Option | Value |");
        sb.AppendLine("|---|---|");
        foreach (var (key, value) in Settings)
            sb.AppendLine($"| {key} | `{value}` |");
        sb.AppendLine();
    }

    private void WriteRestextExclusionsSection(StringBuilder sb)
    {
        if (_restextExclusions.Count == 0) return;

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## Restext Excluded Passages ({_restextExclusions.Count})");
        sb.AppendLine();
        sb.AppendLine("These passages were not scanned for string resources. Their text is emitted as raw strings in the YAML.");
        sb.AppendLine();
        foreach (var passageId in _restextExclusions)
        {
            if (PassageFiles.TryGetValue(passageId, out var file))
                sb.AppendLine($"- [{file}]({file}) (`{passageId}`)");
            else
                sb.AppendLine($"- `{passageId}`");
        }
        sb.AppendLine();
    }

    private void WriteTaggedPassagesSection(StringBuilder sb)
    {
        if (_taggedPassages.Count == 0) return;

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Structure Tags");
        sb.AppendLine();

        foreach (var tag in StructureTags)
        {
            var passages = _taggedPassages
                .Where(kv => kv.Value.Contains(tag, StringComparer.Ordinal))
                .OrderBy(kv => PassageFiles.TryGetValue(kv.Key, out var f) ? f : kv.Key)
                .ToList();
            if (passages.Count == 0) continue;

            sb.AppendLine($"### {tag}");
            sb.AppendLine();
            foreach (var (passageId, _) in passages)
            {
                if (PassageFiles.TryGetValue(passageId, out var file))
                    sb.AppendLine($"- [{file}]({file}) (`{passageId}`)");
                else
                    sb.AppendLine($"- `{passageId}`");
            }
            sb.AppendLine();
        }
    }

    private void WriteInputPromptsSection(StringBuilder sb)
    {
        if (_inputPrompts.Count == 0) return;

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## Input Prompts ({_inputPrompts.Count})");
        sb.AppendLine();
        sb.AppendLine("Player input collected by popup panels.");
        sb.AppendLine();
        sb.AppendLine("| Passage | Variable | Input Type | Prompt |");
        sb.AppendLine("|---|---|---|---|");

        foreach (var p in _inputPrompts.OrderBy(p => p.PassageName))
        {
            var varType = _variables is not null && _variables.TryGetValue(p.StoreIn, out var def)
                ? def.VarType : "?";
            var typeMismatch = (p.InputType == "number" && varType != "int") ||
                               (p.InputType == "string" && varType != "string");
            var varCell = typeMismatch
                ? $"`{p.StoreIn}` [{varType}] ⚠"
                : $"`{p.StoreIn}` [{varType}]";

            string passageCell;
            if (PassageFiles.TryGetValue(p.PassageName, out var file))
            {
                var anchor = p.SourceLine.HasValue ? $"#L{p.SourceLine.Value}" : "";
                passageCell = $"[{p.PassageName}]({file}{anchor})";
            }
            else
            {
                passageCell = p.PassageName;
            }

            sb.AppendLine($"| {passageCell} | {varCell} | {p.InputType} | {EscapeTableCell(p.Text)} |");
        }

        sb.AppendLine();
    }

    private static string EscapeTableCell(string s) => s.Replace("|", "\\|");

    private void WriteOverridesSection(StringBuilder sb)
    {
        if (_overrides.Count == 0) return;

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## Applied Overrides ({_overrides.Count})");
        sb.AppendLine();
        sb.AppendLine("These passages were replaced by hand-authored override YAML files. " +
            "The generated file has been discarded; the override is the authoritative source.");
        sb.AppendLine();

        foreach (var (passageId, fileName, unknownCount) in _overrides.OrderBy(o => o.FileName))
        {
            sb.AppendLine($"- [{fileName}]({fileName}) (`{passageId}`)");
            if (unknownCount == 0)
                sb.AppendLine($"  > ⚠ Possibly stale: the extractor generates 0 unknown nodes for `{passageId}`. " +
                    "Verify the generated output matches the hand-authored version before removing the override.");
        }
        sb.AppendLine();
    }

    private void WriteIsolatedSection(StringBuilder sb)
    {
        if (_isolatedPassages is not { Count: > 0 }) return;

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## Isolated Passages ({_isolatedPassages.Count})");
        sb.AppendLine();
        sb.AppendLine("No other passage links to these passages. They may be intentional entry points, " +
            "test/debug passages, or unreachable dead code.");
        sb.AppendLine();
        sb.AppendLine("> **Editor note:** Identify and warn about passages (or trees) that are " +
            "disconnected from the main passage graph.");
        sb.AppendLine();

        foreach (var name in _isolatedPassages)
        {
            if (PassageFiles.TryGetValue(name, out var file))
                sb.AppendLine($"- [{name}]({file})");
            else
                sb.AppendLine($"- {name}");
        }
        sb.AppendLine();
    }

    private void WriteSection(StringBuilder sb, List<Flag> flags, string kind, string title, string description)
    {
        var items = flags.Where(f => f.Kind == kind).ToList();
        if (items.Count == 0) return;

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## {title} ({items.Count})");
        sb.AppendLine();
        sb.AppendLine(description);
        sb.AppendLine();

        foreach (var group in items.GroupBy(f => f.PassageName).OrderBy(g => g.Key))
        {
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();
            if (PassageFiles.TryGetValue(group.Key, out var yamlFile))
            {
                sb.AppendLine($"[{yamlFile}]({yamlFile})");
                sb.AppendLine();
            }

            foreach (var flag in group)
            {
                // Location line as a file:line link
                if (flag.SourceLine.HasValue)
                {
                    sb.AppendLine($"**{FormatLocation(flag.SourceLine.Value)}**");
                    sb.AppendLine();
                }

                // For unknown_node the detail IS the raw C# code — always use a fenced block.
                // For warning/info the detail is a human-readable message — plain text.
                // If a warning also has a Code field, render that separately as a fenced block.
                if (kind == "unknown_node")
                {
                    sb.AppendLine("```cs");
                    sb.AppendLine(flag.Detail);
                    sb.AppendLine("```");
                }
                else if (flag.Detail.Contains('\n') || flag.Detail.Length > 120)
                {
                    // Long or multi-line detail without a separate code field — treat as code.
                    sb.AppendLine("```cs");
                    sb.AppendLine(flag.Detail);
                    sb.AppendLine("```");
                }
                else
                {
                    sb.AppendLine(flag.Detail);
                }

                // Separate code excerpt (warnings with an explicit Code field)
                if (flag.Code is not null)
                {
                    sb.AppendLine();
                    sb.AppendLine("```cs");
                    sb.AppendLine(flag.Code);
                    sb.AppendLine("```");
                }

                sb.AppendLine();
            }
        }
    }

    // Returns a markdown link "[filename:N](relative/path#LN)" when SourceFilePath and
    // OutputDirPath are set; falls back to plain "filename:N" otherwise.
    private string FormatLocation(int line)
    {
        if (SourceFilePath is null)
            return $"line {line}";

        var fileName = Path.GetFileName(SourceFilePath);
        var display = $"{fileName}:{line}";

        if (OutputDirPath is null)
            return display;

        var relPath = Path.GetRelativePath(OutputDirPath, SourceFilePath).Replace('\\', '/');
        return $"[{display}]({relPath}#L{line})";
    }

    public void PrintSummary()
    {
        Console.WriteLine($"  Passages: {PassagesExtracted}");
        Console.WriteLine($"  Variables: {VariablesDiscovered}");
        if (_restextExclusions.Count > 0)
            Console.WriteLine($"  Restext excluded: {_restextExclusions.Count} passages");
        if (UnknownNodeCount > 0)
            Console.WriteLine($"  Unknown nodes: {UnknownNodeCount} (see report for details)");
        var warnings = _flags.Count(f => f.Kind == "warning");
        if (warnings > 0)
            Console.WriteLine($"  Warnings: {warnings}");
    }
}
