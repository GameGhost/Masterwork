using Masterwork.Engine.Rendering;
using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Rendering;

/// <summary>
/// Finds which input (if any) within a set of node-list regions should receive automatic focus —
/// the first text/number input encountered in document order, recursing into a <see cref="RenderedSection"/>'s
/// own content but never into a nested <see cref="RenderedPopup"/>'s content (a popup computes its
/// own first-focusable input independently, scoped to its own regions — see <c>RenderedPopupView</c>
/// — since it appears/disappears on its own schedule, not the enclosing passage's). Never a boolean
/// (checkbox) input — those aren't something a player types into, so autofocusing one would be
/// surprising rather than helpful.
/// </summary>
public static class FocusHelper
{
    /// <summary>
    /// Scans <paramref name="regions"/> in order (e.g. a passage's chrome header, before-content,
    /// body, after-content, chrome footer) and returns the first eligible input's id, or
    /// <see langword="null"/> if there isn't one.
    /// </summary>
    public static string? FindFirstFocusableInputId(params IEnumerable<RenderedNode>[] regions)
    {
        foreach (var region in regions)
        {
            if (FindFirstFocusableInputId(region) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string? FindFirstFocusableInputId(IEnumerable<RenderedNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case RenderedInput { InputType: not InputValueType.Boolean } input:
                    return input.Id;
                case RenderedSection section when FindFirstFocusableInputId(section.Content) is { } id:
                    return id;
            }
        }

        return null;
    }
}
