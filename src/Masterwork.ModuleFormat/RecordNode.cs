namespace Masterwork.ModuleFormat;

/// <summary>
/// An achievement trigger. The <c>type: record</c> node. Deferred to Phase 3 — the engine
/// currently logs a warning and no-ops at runtime rather than updating achievement state.
/// </summary>
public sealed record RecordNode : Node
{
    /// <inheritdoc/>
    public override string Type => "record";

    /// <summary>Achievement ID, defined in the module manifest.</summary>
    public required string Id { get; init; }
}
