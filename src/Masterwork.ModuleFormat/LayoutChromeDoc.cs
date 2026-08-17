namespace Masterwork.ModuleFormat;

/// <summary>
/// A single <c>layouts/{id}.yaml</c> file: optional node-list regions a module attaches to a
/// <c>layout</c> name (shared by both passages and popups). Purely structural — the app renders
/// whichever of these lists is non-empty wherever the layout name matches; it never branches on
/// what a layout actually means. See <see cref="Masterwork.ModuleFormat.RestextResolver"/> for
/// restext resolution and <c>docs/mws-format-latest.md</c> for the file shape and composition order.
/// </summary>
public sealed record LayoutChromeDoc
{
    /// <summary>The layout name this chrome applies to — authoritative over the filename it was loaded from.</summary>
    public required string LayoutId { get; init; }

    /// <summary>Rendered before everything else — outermost, above the passage/popup's own title or header region.</summary>
    public IReadOnlyList<Node> Header { get; init; } = [];

    /// <summary>Rendered after everything else — outermost, below the passage/popup's own footer region.</summary>
    public IReadOnlyList<Node> Footer { get; init; } = [];

    /// <summary>Rendered immediately before the passage/popup's own body content.</summary>
    public IReadOnlyList<Node> BeforeContent { get; init; } = [];

    /// <summary>Rendered immediately after the passage/popup's own body content.</summary>
    public IReadOnlyList<Node> AfterContent { get; init; } = [];

    /// <summary>SFX-only third resolution tier for anything using this layout — see <see cref="LayoutChromeAudio"/>.</summary>
    public LayoutChromeAudio? Audio { get; init; }
}
