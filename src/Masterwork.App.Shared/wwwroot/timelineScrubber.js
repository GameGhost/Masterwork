// Keeps the highlighted timeline entry (or, reused as-is by StartNewGame.razor, the selected
// module carousel tile) visible without exposing a manual scroll/drag affordance to the player —
// neither list needs to be draggable, they just need the active entry to stay in view (centered
// when there's room, clamped to the nearest edge otherwise, which scrollIntoView's own "center"
// alignment already does for free at either end of the list).
//
// Takes a DOM id (looked up here), not an ElementReference passed in from .NET via @ref — both
// callers render their entries in an @foreach/@for with items that can appear/disappear between
// renders (a new timeline entry, a newly-installed module), and @ref on a conditionally-present
// list item hit a genuine Blazor diffing bug in that situation
// (RenderTreeDiffBuilder.RemoveOldFrame throwing "Unexpected frame type... ElementReferenceCapture")
// that persisted even with a @key on the element. See RenderedInputView.razor's own inputFocus.js
// for the first place this same fix was applied, and its comment for the fuller explanation.
export function centerElement(elementId) {
    const element = document.getElementById(elementId);
    if (!element) {
        return;
    }

    element.scrollIntoView({ behavior: "smooth", inline: "center", block: "nearest" });
}
