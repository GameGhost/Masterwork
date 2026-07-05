using Masterwork.Engine.Expressions;

namespace Masterwork.Engine.Session;

/// <summary>
/// Immutable point-in-time capture. <see cref="Variables"/>/<see cref="SeedOccurrences"/> always
/// reflect state as of just <em>before</em> <see cref="PassageId"/> was rendered — restoring a
/// snapshot and re-rendering its passage reproduces the exact same output deterministically (same
/// starting state -&gt; same assigns -&gt; same randoms).
/// </summary>
public sealed record SessionSnapshot
{
    /// <summary>The passage this snapshot points to.</summary>
    public required string PassageId { get; init; }

    /// <summary>What kind of event produced this snapshot.</summary>
    public required SnapshotKind Kind { get; init; }

    /// <summary>Full session variable state as of just before <see cref="PassageId"/> rendered.</summary>
    public required IReadOnlyDictionary<string, StoryValue> Variables { get; init; }

    /// <summary>Per-seed-key occurrence counters as of just before <see cref="PassageId"/> rendered — see <see cref="SessionPrng"/>.</summary>
    public required IReadOnlyDictionary<string, int> SeedOccurrences { get; init; }

    /// <summary>The value submitted, for an <see cref="SnapshotKind.InputReceived"/> snapshot.</summary>
    public StoryValue? SubmittedInput { get; init; }

    /// <summary>Human-readable label for the timeline scrubber, if any.</summary>
    public string? DisplayLabel { get; init; }

    /// <summary>Machine-readable label for diagnostics/test assertions, if any.</summary>
    public string? DiagnosticLabel { get; init; }
}
