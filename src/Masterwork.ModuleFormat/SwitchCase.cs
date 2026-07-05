namespace Masterwork.ModuleFormat;

/// <summary>One <c>match</c>/<c>nodes</c> case of a <see cref="SwitchNode"/>.</summary>
public sealed record SwitchCase
{
    /// <summary>
    /// The match value: an <see cref="int"/>, a <see cref="string"/>, a <c>restext://Key</c>
    /// reference (pre-resolution), a list of those (any-of), or a pattern string (e.g. <c>"&gt;3"</c>).
    /// </summary>
    public required object Match { get; init; }

    /// <summary>Nodes rendered when this case matches.</summary>
    public required IReadOnlyList<Node> Nodes { get; init; }
}
