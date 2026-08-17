namespace Masterwork.Engine.Rendering;

/// <summary>
/// The rendered form of a module's <c>layouts/{id}.yaml</c> chrome for whichever layout a passage
/// or popup declared — see <see cref="PassageRenderResult.Chrome"/> and
/// <see cref="RenderedPopup.Chrome"/>. Always present (never null); all four lists are simply empty
/// when the module has no chrome registered for that layout name.
/// </summary>
public sealed record RenderedLayoutChrome(
    IReadOnlyList<RenderedNode> Header,
    IReadOnlyList<RenderedNode> Footer,
    IReadOnlyList<RenderedNode> BeforeContent,
    IReadOnlyList<RenderedNode> AfterContent)
{
    /// <summary>No chrome registered for this layout — all four regions empty.</summary>
    public static readonly RenderedLayoutChrome Empty = new([], [], [], []);

    /// <summary>Resolved SFX defaults for this layout, if it declared any — see <see cref="RenderedLayoutAudio"/>. <see langword="null"/> for <see cref="Empty"/> and any layout with no <c>audio:</c> block of its own.</summary>
    public RenderedLayoutAudio? Audio { get; init; }
}
