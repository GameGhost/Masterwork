// Keeps the highlighted timeline entry visible without exposing a manual scroll/drag affordance to
// the player — the scrubber never needs to be draggable, it just needs the current entry to stay in
// view (centered when there's room, clamped to the nearest edge otherwise, which scrollIntoView's
// own "center" alignment already does for free at either end of the list).
export function centerElement(element) {
    if (!element) {
        return;
    }

    element.scrollIntoView({ behavior: "smooth", inline: "center", block: "nearest" });
}
