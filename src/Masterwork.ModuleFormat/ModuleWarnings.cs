using System.Collections.Generic;

namespace Masterwork.ModuleFormat;

public sealed record ModuleWarning(string Kind, string Message);

// Warnings collected at module load time — missing restext keys, unresolved passage references,
// invalid expressions. Distinct from extractor warnings, which live in _extraction-report.md.
public sealed class ModuleWarnings
{
    private readonly List<ModuleWarning> _items = [];
    public IReadOnlyList<ModuleWarning> Items => _items;
    public void Add(string kind, string message) => _items.Add(new ModuleWarning(kind, message));
}
