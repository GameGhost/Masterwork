namespace Masterwork.ModuleFormat;

/// <summary>Unconditional navigation with no player interaction. The <c>type: goto</c> node.</summary>
public sealed record GotoNode : Node
{
    /// <inheritdoc/>
    public override string Type => "goto";

    /// <summary>Target passage_id, or <c>"${expr}"</c> for a dynamic target, resolved immediately at render time.</summary>
    public required string Target { get; init; }

    /// <summary>
    /// Overrides whether this <c>goto</c>'s own navigation creates a timeline snapshot — only takes
    /// effect when it fires from within a <c>link</c>'s <c>onclick</c> or a <c>popup</c>'s
    /// <c>onclose</c>, preempting the enclosing action's own <c>target</c> (and, when set here, its
    /// own <c>snapshot</c> decision too — not just the label). <see langword="null"/> (the field
    /// absent) means "inherit whatever the enclosing action's own <c>snapshot</c> says" — the only
    /// behavior a plain top-level <c>goto</c> has, since it has no enclosing action to override.
    /// Parsed from <c>snapshot</c>, using the same bare-bool-or-string convention as <c>link</c>/
    /// <c>popup</c>'s own <c>snapshot</c> field (see <see cref="SnapshotLabel"/>).
    /// </summary>
    public bool? StateAffecting { get; init; }

    /// <summary>
    /// Display label for the timeline scrubber entry. Set by writing a string (instead of a bare
    /// bool) to <c>snapshot</c> — that string is both the label and an implicit
    /// <see langword="true"/> for <see cref="StateAffecting"/>. Takes priority over the enclosing
    /// <c>link</c>/<c>popup</c>'s own label, since this <c>goto</c> is what actually picked the
    /// destination.
    /// </summary>
    public string? SnapshotLabel { get; init; }
}
