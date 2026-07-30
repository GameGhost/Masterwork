// F11 fullscreen toggle for the Windows/MAUI head — see FullscreenToggle.cs's own remarks for why
// this is a page-level listener calling back into .NET, not a native WinUI KeyboardAccelerator.
// A plain document-level listener (not a component-scoped one imported via IJSRuntime) so it's
// active immediately on page load and independent of whatever Blazor content happens to have
// focus at the time — the whole point is that it must work no matter where focus is.
document.addEventListener("keydown", (e) => {
    if (e.key !== "F11" || e.repeat) {
        return;
    }

    e.preventDefault();
    DotNet.invokeMethodAsync("Masterwork.App", "Toggle");
});
