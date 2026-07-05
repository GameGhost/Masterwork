namespace Masterwork.ModuleFormat;

/// <summary>A named timeline milestone bookmark. The <c>type: checkpoint</c> node.</summary>
public sealed record CheckpointNode : Node
{
    /// <inheritdoc/>
    public override string Type => "checkpoint";

    /// <summary>Stable checkpoint identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable label for the timeline scrubber, if any.</summary>
    public string? Display { get; init; }

    /// <summary>Machine-readable label for test assertions/diagnostics, if any.</summary>
    public string? Diagnostic { get; init; }
}
