using Masterwork.Engine.Expressions;

namespace Masterwork.Engine.Session;

/// <summary>
/// Captures the live edge's most recent non-state-affecting (<c>RenderInPlace</c>) render — one
/// that diverged from its timeline entry's own anchor snapshot without creating a bookmark of its
/// own, e.g. a chain of automatic tie-break rounds. Shaped like <see cref="SessionSnapshot"/> (same
/// "state as of just before <see cref="PassageId"/> renders" convention, so restoring and
/// re-rendering reproduces it exactly), but deliberately a distinct type: it's never added to
/// <see cref="GameSession.Timeline"/>, and unlike a real entry there's always at most one — a later
/// in-place transition simply overwrites it rather than accumulating its own history. Survives
/// stepping back through any amount of real history (see <see cref="GameSession.StepBack"/>'s own
/// remarks) — only <see cref="GameSession.ResumeFromHere"/> (branching play from a historical
/// point) or a new state-affecting navigation (which supersedes it with a real snapshot) discard
/// it.
/// </summary>
public sealed record ActiveState
{
    /// <summary>The passage this active state last landed on.</summary>
    public required string PassageId { get; init; }

    /// <summary>Full session variable state as of just before <see cref="PassageId"/> rendered.</summary>
    public required IReadOnlyDictionary<string, StoryValue> Variables { get; init; }

    /// <summary>Per-seed-key occurrence counters as of just before <see cref="PassageId"/> rendered — see <see cref="SessionPrng"/>.</summary>
    public required IReadOnlyDictionary<string, int> SeedOccurrences { get; init; }
}
