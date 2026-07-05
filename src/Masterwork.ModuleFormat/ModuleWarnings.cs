namespace Masterwork.ModuleFormat;

/// <summary>
/// Warnings collected at module load time — missing restext keys, unresolved passage references,
/// unmatched/wrong-shaped fields, unknown node types. Distinct from extractor warnings, which live
/// in <c>_extraction-report.md</c> and are produced at extraction time rather than load time.
/// </summary>
public sealed class ModuleWarnings
{
    private readonly List<ModuleWarning> _items = [];

    /// <summary>All warnings recorded so far, in the order they were added.</summary>
    public IReadOnlyList<ModuleWarning> Items => _items;

    /// <summary>Records a new warning.</summary>
    /// <param name="kind">Short machine-readable category.</param>
    /// <param name="message">Human-readable description.</param>
    public void Add(string kind, string message)
    {
        _items.Add(new ModuleWarning(kind, message));
    }
}
