namespace Masterwork.ModuleFormat;

/// <summary>
/// A fully loaded module: every passage parsed and restext-resolved, the variable manifest, the
/// locale dictionary, and any warnings accumulated while loading. Built by
/// <see cref="Masterwork.ModuleFormat.ModuleLoader"/>.
/// </summary>
public sealed class LoadedModule
{
    /// <summary>All passages, keyed by <c>passage_id</c>.</summary>
    public required IReadOnlyDictionary<string, MwsPassageDoc> Passages { get; init; }

    /// <summary>Session and standard variable definitions, keyed by name.</summary>
    public required IReadOnlyDictionary<string, VarDef> Variables { get; init; }

    /// <summary>The <c>en-US.restext</c> locale dictionary, keyed by restext key.</summary>
    public required IReadOnlyDictionary<string, string> Locale { get; init; }

    /// <summary>Issues recorded while loading — missing fields, unresolved references, unknown node types, etc.</summary>
    public required ModuleWarnings Warnings { get; init; }

    /// <summary>The passage tagged <c>Begins-Here</c> (case-insensitive), or <see langword="null"/> if none is tagged.</summary>
    public string? StartPassageId { get; init; }
}
