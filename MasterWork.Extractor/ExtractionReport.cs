using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MasterWork.Extractor;

public class ExtractionReport
{
    private readonly record struct Flag(
        string PassageName,
        string Kind,
        string Detail,
        string? Code = null,
        int? SourceLine = null);

    private readonly List<Flag> _flags = [];

    public int PassagesExtracted { get; set; }
    public int VariablesDiscovered { get; set; }
    public int UnknownNodeCount => _flags.Count(f => f.Kind == "unknown_node" &&
        !_overrides.Any(o => o.PassageId == f.PassageName));

    // Set before Write() so the report can generate relative-path links.
    public string? SourceFilePath { get; set; }
    public string? OutputDirPath { get; set; }

    // passage name → output filename (just the filename, report is in the same dir)
    public Dictionary<string, string> PassageFiles { get; } = new(StringComparer.Ordinal);

    private List<string>? _isolatedPassages;
    public void AddIsolatedPassages(List<string> names) => _isolatedPassages = names;

    private readonly List<(string PassageId, string FileName)> _overrides = [];
    public void AddOverrideApplied(string passageId, string fileName) =>
        _overrides.Add((passageId, fileName));

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

    public void Write(string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extraction Report");
        sb.AppendLine();

        // Flags for overridden passages reflect the discarded generated file — suppress them.
        var overriddenIds = new HashSet<string>(_overrides.Select(o => o.PassageId), StringComparer.Ordinal);
        var activeFlags = _flags.Where(f => !overriddenIds.Contains(f.PassageName)).ToList();

        var unknownCount = activeFlags.Count(f => f.Kind == "unknown_node");
        var warnCount = activeFlags.Count(f => f.Kind == "warning");
        var infoCount = activeFlags.Count(f => f.Kind == "info");
        var isolatedCount = _isolatedPassages?.Count ?? 0;
        var overrideCount = _overrides.Count;

        sb.AppendLine("| | |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| Passages extracted | **{PassagesExtracted}** |");
        sb.AppendLine($"| Variables discovered | **{VariablesDiscovered}** |");
        sb.AppendLine($"| Unknown nodes | **{unknownCount}** |");
        sb.AppendLine($"| Warnings | **{warnCount}** |");
        sb.AppendLine($"| Info | **{infoCount}** |");
        if (isolatedCount > 0)
            sb.AppendLine($"| Isolated passages | **{isolatedCount}** |");
        if (overrideCount > 0)
            sb.AppendLine($"| Overrides applied | **{overrideCount}** |");
        sb.AppendLine();

        WriteOverridesSection(sb);
        WriteSection(sb, activeFlags, "unknown_node", "Unknown Nodes",
            "Unrecognized statements — emitted as `type: unknown` in the passage YAML. Each requires manual review.");
        WriteSection(sb, activeFlags, "warning", "Warnings",
            "Recognized patterns that required a fallback or approximation.");
        WriteSection(sb, activeFlags, "info", "Info",
            "Informational notes — no action required.");
        WriteIsolatedSection(sb);

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"Report written to: {outputPath}");
    }

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

        foreach (var (passageId, fileName) in _overrides.OrderBy(o => o.FileName))
        {
            sb.AppendLine($"- [{fileName}]({fileName}) (`{passageId}`)");
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
        if (UnknownNodeCount > 0)
            Console.WriteLine($"  Unknown nodes: {UnknownNodeCount} (see report for details)");
        var warnings = _flags.Count(f => f.Kind == "warning");
        if (warnings > 0)
            Console.WriteLine($"  Warnings: {warnings}");
    }
}
