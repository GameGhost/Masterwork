using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MasterWork.Extractor;

public class ExtractionReport
{
    private readonly record struct Flag(string PassageName, string Kind, string Detail);
    private readonly List<Flag> _flags = [];

    public int PassagesExtracted { get; set; }
    public int VariablesDiscovered { get; set; }
    public int UnknownNodeCount => _flags.Count(f => f.Kind == "unknown_node");

    public void AddUnhandled(string passageName, string code) =>
        _flags.Add(new(passageName, "unknown_node", code.Length > 120 ? code[..120] + "…" : code));

    public void AddWarning(string passageName, string message) =>
        _flags.Add(new(passageName, "warning", message));

    public void AddInfo(string passageName, string message) =>
        _flags.Add(new(passageName, "info", message));

    public void Write(string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extraction Report");
        sb.AppendLine();
        sb.AppendLine($"- Passages extracted: {PassagesExtracted}");
        sb.AppendLine($"- Variables discovered: {VariablesDiscovered}");
        sb.AppendLine($"- Unknown nodes (require human review): {UnknownNodeCount}");
        sb.AppendLine();

        var byKind = _flags.GroupBy(f => f.Kind).OrderBy(g => g.Key);
        foreach (var group in byKind)
        {
            sb.AppendLine($"## {group.Key} ({group.Count()})");
            sb.AppendLine();
            foreach (var f in group.OrderBy(f => f.PassageName))
            {
                sb.AppendLine($"**{f.PassageName}**: `{f.Detail}`");
            }
            sb.AppendLine();
        }

        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"Report written to: {outputPath}");
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
