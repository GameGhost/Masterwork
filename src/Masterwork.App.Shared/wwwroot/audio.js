let audioCtx = null;
let ambientOnly = false;
let currentTrack = null; // { gain, stop }

function ensureContext() {
    audioCtx ??= new (window.AudioContext || window.webkitAudioContext)();
    return audioCtx;
}

function targetGain() {
    return ambientOnly ? 0.15 : 1.0;
}

function makeSource(ctx, url) {
    if (url === "synth://tone") {
        const osc = ctx.createOscillator();
        osc.frequency.value = 220;
        osc.start();
        return { node: osc, stop: () => osc.stop() };
    }

    const audio = new Audio(url);
    audio.loop = true;
    audio.play();
    const node = ctx.createMediaElementSource(audio);
    return { node, stop: () => audio.pause() };
}

export function playBgm(url, crossfadeSeconds = 1.5) {
    const ctx = ensureContext();
    const gain = ctx.createGain();
    gain.gain.setValueAtTime(0, ctx.currentTime);
    gain.connect(ctx.destination);

    const { node, stop } = makeSource(ctx, url);
    node.connect(gain);
    gain.gain.linearRampToValueAtTime(targetGain(), ctx.currentTime + crossfadeSeconds);

    const previous = currentTrack;
    currentTrack = { gain, stop };

    if (previous) {
        previous.gain.gain.linearRampToValueAtTime(0, ctx.currentTime + crossfadeSeconds);
        setTimeout(() => previous.stop(), crossfadeSeconds * 1000 + 100);
    }
}

export function stopBgm(fadeSeconds = 1.0) {
    if (!currentTrack) {
        return;
    }

    const ctx = ensureContext();
    currentTrack.gain.gain.linearRampToValueAtTime(0, ctx.currentTime + fadeSeconds);
    const toStop = currentTrack;
    currentTrack = null;
    setTimeout(() => toStop.stop(), fadeSeconds * 1000 + 100);
}

export function playSfx(url) {
    const ctx = ensureContext();

    if (url === "synth://blip") {
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.frequency.value = 880;
        gain.gain.setValueAtTime(0.5, ctx.currentTime);
        osc.connect(gain);
        gain.connect(ctx.destination);
        osc.start();
        osc.stop(ctx.currentTime + 0.15);
        return;
    }

    new Audio(url).play();
}

export function setAmbientOnly(value) {
    ambientOnly = value;
    if (currentTrack) {
        const ctx = ensureContext();
        currentTrack.gain.gain.linearRampToValueAtTime(targetGain(), ctx.currentTime + 0.5);
    }
}
