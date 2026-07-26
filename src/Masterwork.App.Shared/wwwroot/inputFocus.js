// Focuses the first input in a newly-rendered passage/popup without popping the on-screen keyboard
// on mobile. An earlier version tried to detect "mobile" via matchMedia("(pointer: coarse)") and
// skip focus() entirely there — dropped because that media feature reflects the browser's *primary*
// pointer heuristic, which is unreliable on hybrid devices (a laptop with a touchscreen can report
// "coarse" even though a mouse/keyboard is what's actually in use), so it was skipping focus on
// plain desktop machines too. This uses a different, well-established technique instead: mark the
// field readonly *before* focusing it (a readonly field can receive focus without the on-screen
// keyboard appearing on mobile browsers), then remove readonly on the next tick. Desktop is
// unaffected either way — the field is focused immediately and editable a moment later, well before
// a human could react and start typing.
export function focusWithoutMobileKeyboard(element) {
    if (!element) {
        return;
    }

    const wasReadOnly = element.hasAttribute("readonly");
    element.setAttribute("readonly", "readonly");
    element.focus();

    setTimeout(() => {
        if (!wasReadOnly) {
            element.removeAttribute("readonly");
        }
    }, 0);
}
