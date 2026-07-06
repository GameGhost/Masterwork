// 6 fixed stops (0-5), matching AppSettings.TextSizeStep — step 2 is 1.0 (normal) size.
const SCALES = [0.8, 0.9, 1.0, 1.15, 1.3, 1.5];

export function applyTextScale(step) {
    const scale = SCALES[Math.max(0, Math.min(SCALES.length - 1, step))];
    document.documentElement.style.setProperty("--mws-text-scale", scale);
}
