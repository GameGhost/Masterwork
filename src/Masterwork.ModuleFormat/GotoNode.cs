namespace Masterwork.ModuleFormat;

/// <summary>Unconditional navigation with no player interaction and no timeline snapshot. The <c>type: goto</c> node.</summary>
public sealed record GotoNode : Node
{
    /// <inheritdoc/>
    public override string Type => "goto";

    /// <summary>Target passage_id, or <c>"${expr}"</c> for a dynamic target, resolved immediately at render time.</summary>
    public required string Target { get; init; }

    /// <summary>
    /// Custom label for the timeline scrubber entry — only takes effect when this <c>goto</c> fires
    /// from within a <c>link</c>'s <c>onclick</c> or a <c>popup</c>'s <c>onclose</c>, preempting its
    /// enclosing action's own <c>target</c> (and, when set, taking priority over that action's own
    /// <c>snapshot</c> label, since the goto picked the actual destination). Has no effect on a
    /// plain top-level <c>goto</c>, which never creates a snapshot.
    /// </summary>
    public string? SnapshotLabel { get; init; }
}
