let audioCtx = null;
let ambientOnly = false;
let bgmVolume = 1.0;
let bgmMuted = false;
let sfxVolume = 1.0;
let sfxMuted = false;

// The currently-sounding background track (single override, or whichever playlist entry is
// currently playing) — { gain, stop }. currentPlaylist drives auto-advance when non-null; a plain
// playBgm() call (a passage/popup single-track override) always clears it, since only the module
// tier can ever be a playlist (see Masterwork.Engine.Audio.AudioResolution).
let currentBgm = null;
let currentPlaylist = null; // { urls, order, queue, index }

// audio_track elements, keyed by an opaque handle (their RenderedAudioTrack action id) — each gets
// its own persistent GainNode (unlike playSfx's one-shot fire-and-forget) so live SfxVolume/SfxMuted
// changes apply while a track is open and playing, not just at the moment it started.
const tracks = new Map(); // handle -> { audio, gain, bgmBehavior, dotNetRef, dotNetMethod }
const duckingTracks = new Set(); // handles currently applying bgm_behavior "duck"
const pausingTracks = new Set(); // handles currently applying bgm_behavior "pause"

function ensureContext() {
    audioCtx ??= new (window.AudioContext || window.webkitAudioContext)();
    return audioCtx;
}

function targetGain() {
    if (bgmMuted) {
        return 0;
    }
    if (pausingTracks.size > 0) {
        return 0;
    }

    let base = ambientOnly ? 0.15 : 1.0;
    if (duckingTracks.size > 0) {
        base *= 0.25;
    }
    return base * bgmVolume;
}

function sfxGain() {
    return sfxMuted ? 0 : sfxVolume;
}

function makeLoopingSource(ctx, url) {
    const audio = new Audio(url);
    audio.loop = true;
    audio.play();
    const node = ctx.createMediaElementSource(audio);
    return { node, stop: () => audio.pause() };
}

function startBgmSource({ node, stop }, crossfadeSeconds) {
    const ctx = ensureContext();
    const gain = ctx.createGain();
    gain.gain.setValueAtTime(0, ctx.currentTime);
    gain.connect(ctx.destination);
    node.connect(gain);
    gain.gain.linearRampToValueAtTime(targetGain(), ctx.currentTime + crossfadeSeconds);

    const previous = currentBgm;
    currentBgm = { gain, stop };

    if (previous) {
        previous.gain.gain.linearRampToValueAtTime(0, ctx.currentTime + crossfadeSeconds);
        setTimeout(() => previous.stop(), crossfadeSeconds * 1000 + 100);
    }
}

export function playBgm(url, crossfadeSeconds = 1.5) {
    currentPlaylist = null;
    startBgmSource(makeLoopingSource(ensureContext(), url), crossfadeSeconds);
}

export function playBgmPlaylist(urls, order = 'sequence', crossfadeSeconds = 1.5) {
    if (!urls || urls.length === 0) {
        stopBgm();
        return;
    }

    if (urls.length === 1) {
        // Nothing to advance to — behaves exactly like a single looping track.
        playBgm(urls[0], crossfadeSeconds);
        return;
    }

    const queue = order === 'shuffle' ? shuffled(urls) : [...urls];
    currentPlaylist = { urls, order, queue, index: 0 };
    playCurrentPlaylistTrack(crossfadeSeconds);
}

function shuffled(arr) {
    const a = [...arr];
    for (let i = a.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [a[i], a[j]] = [a[j], a[i]];
    }
    return a;
}

function playCurrentPlaylistTrack(crossfadeSeconds) {
    const playlist = currentPlaylist;
    if (!playlist) {
        return;
    }

    const ctx = ensureContext();
    const audio = new Audio(playlist.queue[playlist.index]);
    // Deliberately non-looping — this is what lets `ended` fire at all and drive auto-advance.
    // playBgm's single-track path is the opposite: it loops, so `ended` never fires there.
    audio.loop = false;
    audio.addEventListener('ended', () => {
        if (currentPlaylist !== playlist) {
            return; // a newer resolution winner has already superseded this playlist
        }
        advancePlaylist(playlist);
    });
    audio.play();
    const node = ctx.createMediaElementSource(audio);
    startBgmSource({ node, stop: () => audio.pause() }, crossfadeSeconds);
}

function advancePlaylist(playlist) {
    playlist.index++;
    if (playlist.index >= playlist.queue.length) {
        playlist.index = 0;
        if (playlist.order === 'shuffle') {
            playlist.queue = shuffled(playlist.urls);
        }
    }
    playCurrentPlaylistTrack(0.5); // a short crossfade between playlist entries, not a hard cut
}

export function stopBgm(fadeSeconds = 1.0) {
    currentPlaylist = null;
    if (!currentBgm) {
        return;
    }

    const ctx = ensureContext();
    currentBgm.gain.gain.linearRampToValueAtTime(0, ctx.currentTime + fadeSeconds);
    const toStop = currentBgm;
    currentBgm = null;
    setTimeout(() => toStop.stop(), fadeSeconds * 1000 + 100);
}

export function playSfx(url) {
    const audio = new Audio(url);
    audio.volume = sfxGain();
    audio.play();
}

export function setAmbientOnly(value) {
    ambientOnly = value;
    applyBgmGain();
}

export function setBgmVolume(value) {
    bgmVolume = value;
    applyBgmGain();
}

export function setBgmMuted(value) {
    bgmMuted = value;
    applyBgmGain();
}

export function setSfxVolume(value) {
    sfxVolume = value;
    applyAllTrackGains();
}

export function setSfxMuted(value) {
    sfxMuted = value;
    applyAllTrackGains();
}

function applyBgmGain() {
    if (currentBgm) {
        const ctx = ensureContext();
        currentBgm.gain.gain.linearRampToValueAtTime(targetGain(), ctx.currentTime + 0.5);
    }
}

function applyAllTrackGains() {
    const ctx = ensureContext();
    for (const track of tracks.values()) {
        track.gain.gain.linearRampToValueAtTime(sfxGain(), ctx.currentTime + 0.2);
    }
}

// ── Addressable per-track playback (audio_track elements) ──────────────────

export function loadTrack(handle, url, bgmBehavior) {
    disposeTrack(handle); // idempotent re-load, e.g. a passage re-render with the same handle

    const ctx = ensureContext();
    const audio = new Audio(url);
    audio.loop = false;
    const gain = ctx.createGain();
    gain.gain.setValueAtTime(sfxGain(), ctx.currentTime);
    const node = ctx.createMediaElementSource(audio);
    node.connect(gain);
    gain.connect(ctx.destination);

    const track = { audio, gain, bgmBehavior: bgmBehavior || 'none', dotNetRef: null, dotNetMethod: null };
    tracks.set(handle, track);

    audio.addEventListener('play', () => applyTrackBgmBehavior(handle, true));
    audio.addEventListener('pause', () => applyTrackBgmBehavior(handle, false));
    audio.addEventListener('ended', () => {
        applyTrackBgmBehavior(handle, false);
        if (track.dotNetRef) {
            track.dotNetRef.invokeMethodAsync(track.dotNetMethod);
        }
    });
}

export function playTrack(handle) {
    tracks.get(handle)?.audio.play();
}

export function pauseTrack(handle) {
    tracks.get(handle)?.audio.pause();
}

export function seekTrack(handle, seconds) {
    const track = tracks.get(handle);
    if (track) {
        track.audio.currentTime = seconds;
    }
}

export function getTrackStatus(handle) {
    const track = tracks.get(handle);
    if (!track) {
        return { isPlaying: false, positionSeconds: 0, durationSeconds: 0 };
    }

    return {
        isPlaying: !track.audio.paused && !track.audio.ended,
        positionSeconds: track.audio.currentTime || 0,
        durationSeconds: Number.isFinite(track.audio.duration) ? track.audio.duration : 0,
    };
}

export function disposeTrack(handle) {
    const track = tracks.get(handle);
    if (!track) {
        return;
    }

    track.audio.pause();
    applyTrackBgmBehavior(handle, false);
    tracks.delete(handle);
}

// dotNetRef is a Blazor DotNetObjectReference; callbackMethodName must name a public [JSInvokable]
// no-argument method on it. Invoked once when the track reaches its natural end — not on a manual
// pauseTrack() call, matching how playCurrentPlaylistTrack's own `ended` listener only advances on
// a real end, never a pause.
export function setTrackEndedCallback(handle, dotNetRef, callbackMethodName) {
    const track = tracks.get(handle);
    if (track) {
        track.dotNetRef = dotNetRef;
        track.dotNetMethod = callbackMethodName;
    }
}

function applyTrackBgmBehavior(handle, isPlaying) {
    const track = tracks.get(handle);
    const set = track?.bgmBehavior === 'duck' ? duckingTracks : track?.bgmBehavior === 'pause' ? pausingTracks : null;
    if (!set) {
        return;
    }

    if (isPlaying) {
        set.add(handle);
    } else {
        set.delete(handle);
    }
    applyBgmGain();
}
