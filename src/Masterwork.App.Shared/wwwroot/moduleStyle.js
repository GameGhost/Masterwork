const STYLE_ELEMENT_ID = "mws-module-style";

// Sets (or clears) the single <style> element carrying the currently-loaded module's own CSS.
// The app's shared chrome never puts layout/style-specific rules in its own stylesheet — see
// GameSessionState.StyleCss / AssetResolver's bundle-local tier for the rest of this mechanism.
export function setModuleStyle(css) {
    let el = document.getElementById(STYLE_ELEMENT_ID);
    if (!css) {
        el?.remove();
        return;
    }

    if (!el) {
        el = document.createElement("style");
        el.id = STYLE_ELEMENT_ID;
        document.head.appendChild(el);
    }

    el.textContent = css;
}
