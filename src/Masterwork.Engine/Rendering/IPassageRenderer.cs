using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Rendering;

/// <summary>
/// Walks a passage's node tree, applying assign/let mutations to a <see cref="VariableStore"/> as
/// it goes (in document order) and producing a flat rendered-node tree for the UI.
/// </summary>
public interface IPassageRenderer
{
    /// <summary>Renders a passage to a flat node/action tree.</summary>
    /// <param name="passage">The passage to render.</param>
    /// <param name="store">Variable store to read from and mutate via <c>assign</c>/<c>let</c> nodes.</param>
    /// <param name="module">The loaded module, used to resolve <c>include_passage</c> targets.</param>
    /// <param name="visitedPassageIds">Passages considered "visited" for <c>check_progress</c> validation.</param>
    /// <exception cref="CheckProgressViolationException">The passage's <c>check_progress</c> prerequisite hasn't been visited.</exception>
    PassageRenderResult Render(MwsPassageDoc passage, VariableStore store, LoadedModule module, IReadOnlySet<string> visitedPassageIds);

    /// <summary>
    /// Renders a raw node list in isolation — used for popup content on open, a <c>link</c>'s
    /// <c>onclick</c>, a popup's <c>onclose</c>, and <c>include_passage</c> inlining.
    /// </summary>
    /// <param name="actionIdPrefix">
    /// Prefixed onto every action ID produced by this render, so IDs from a nested render (e.g.
    /// popup content) can't collide with the enclosing passage's own action IDs.
    /// </param>
    NodeListRenderResult RenderNodeList(IReadOnlyList<Node> nodes, VariableStore store, LoadedModule module, string actionIdPrefix = "");
}
