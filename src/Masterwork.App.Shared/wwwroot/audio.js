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

// True while the app itself (not the player) has intentionally paused bgm — the app losing
// foreground focus (backgrounded on mobile, deactivated on desktop) or, on the web build, the tab
// becoming hidden. See pauseBgmForBackground/resumeBgmFromBackground below.
let backgroundPaused = false;

// audio_track elements, keyed by an opaque handle (their RenderedAudioTrack action id) — each gets
// its own persistent GainNode (unlike playSfx's one-shot fire-and-forget) so live SfxVolume/SfxMuted
// changes apply while a track is open and playing, not just at the moment it started.
const tracks = new Map(); // handle -> { audio, gain, bgmBehavior, dotNetRef, dotNetMethod }
const duckingTracks = new Set(); // handles currently applying bgm_behavior "duck"
const pausingTracks = new Set(); // handles currently applying bgm_behavior "pause"

// Always-on but quiet (console.debug — hidden under Chrome DevTools' default "Info" level, shown
// once "Verbose" is checked) diagnostic trail for bgm state transitions. Added chasing a
// player-reported bug (background music restarting/looping every ~3s, seemingly tied to
// backgrounding the tab for a while then refocusing it) that static code reading couldn't pin down
// — every plausible redundant-restart path already found and closed (see playBgm's dedup guard and
// startBgmSource's immediate cancelPending below) didn't fully explain it, so the next real
// reproduction needs an actual trail: open devtools, enable Verbose, reproduce, read back what
// actually fired and in what order.
//
// Also forwarded into the .NET-side file log (JsAudioLogBridge, MAUI heads only) — a real Android
// bug (silent bgm at boot) turned out to need exactly this trail, but devtools access on a locked
// device isn't realistic for a player to provide; the file log already is (see CLAUDE.md's logging
// table). Fire-and-forget and defensively guarded: DotNet may not be defined yet during the very
// first script evaluation, and Web/WASM heads never call JsAudioLogBridge.Configure() at all (no
// filesystem there either), so the interop call harmlessly resolves to a no-op either way.
function logAudio(...args) {
    console.debug('[masterwork-audio]', new Date().toISOString().slice(11, 23), ...args);
    if (typeof DotNet !== 'undefined') {
        const message = args.map(a => typeof a === 'string' ? a : JSON.stringify(a)).join(' ');
        // Logged, not silently swallowed — a bridge failure (trimmed-away Log method, interop not
        // ready yet) should be visible to whoever has devtools access, even though it wasn't to the
        // player who reported the original bug this bridge exists for.
        DotNet.invokeMethodAsync('Masterwork.App.Shared', 'Log', 'debug', message)
            .catch(err => console.error('[masterwork-audio] JsAudioLogBridge interop failed:', err));
    }
}

function ensureContext() {
    const isNew = !audioCtx;
    audioCtx ??= new (window.AudioContext || window.webkitAudioContext)();
    if (isNew) {
        audioCtx.addEventListener('statechange', () => logAudio('audioCtx statechange ->', audioCtx.state));
    }
    // A freshly-constructed AudioContext starts "suspended" until explicitly resumed — playSfx
    // never hits this (it plays a plain <audio> element with no Web Audio routing at all, so it's
    // unaffected), but anything routed through this context (playBgm, playlists, addressable
    // tracks) produces genuinely silent output while suspended, even though the underlying
    // <audio> element itself is happily "playing" (currentTime advancing) — the browser just isn't
    // pulling samples out of the graph. Not awaited: connections/automation scheduled below are
    // valid regardless of resume timing, since ctx.currentTime simply resumes advancing once this
    // resolves.
    if (audioCtx.state === 'suspended') {
        audioCtx.resume();
    }
    return audioCtx;
}

// Chrome (and others) can auto-suspend a backgrounded tab's AudioContext as a power-saving
// measure — explicitly resuming the moment the tab becomes visible again is the documented
// mitigation. Also the web build's own trigger for pauseBgmForBackground/resumeBgmFromBackground
// below (declared further down this file) — the MAUI heads use native app-lifecycle events
// instead (AppLifecycleAudioBridge), since visibilitychange inside an embedded WebView doesn't
// reliably fire when the *native* app is backgrounded, only when the page itself is hidden within
// it, which isn't the same thing. In an actual browser tab, hidden/visible IS the right signal.
document.addEventListener('visibilitychange', () => {
    logAudio('visibilitychange ->', document.visibilityState, 'audioCtx.state =', audioCtx?.state, 'currentBgm.url =', currentBgm?.url);
    if (document.visibilityState === 'hidden') {
        pauseBgmForBackground();
        return;
    }

    if (audioCtx && audioCtx.state === 'suspended') {
        audioCtx.resume();
    }
    resumeBgmFromBackground();
});

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

// Browsers refuse audio.play() until the page has seen a real user gesture (click/touch/key) —
// on a fresh load/refresh this rejects every autoplay attempt (module bgm, on_display SFX,
// autoplay audio_track) with an unhandled NotAllowedError, and nothing ever plays until the
// player happens to navigate somewhere. Every .play() call in this file routes through
// safePlay() below: on that specific rejection, the element is parked in pendingPlays for
// attachUnlockListener's own retry.
//
// attachUnlockListener() itself is installed UNCONDITIONALLY at module load (see the call at the
// bottom of this block) — not only reactively from a caught rejection. Android's WebView
// (Chromium) turns out to need this even though it does NOT reject audio.play() the way desktop
// browsers do (confirmed: button/SFX clicks — plain playSfx(), no AudioContext involved — worked
// immediately on a real device). What's actually gated there is AudioContext.resume() itself:
// Chromium's autoplay policy requires resume() to run synchronously inside a trusted user-gesture
// event handler, and every call site in this file that reaches ensureContext() (app boot's theme
// bgm, a module's own bgm sync on passage render) does so from a Blazor/JS-interop callback, not
// a native DOM event — so resume() silently no-ops there, the AudioContext stays 'suspended'
// forever, and everything routed through it (bgm, playlists, audio_track) stays genuinely silent
// even though the underlying <audio> elements are happily "playing" and gain/mute changes are
// applied correctly to a graph nobody can hear. Reactive-only attachment (the old behavior) never
// even got a chance to fire on Android, since nothing there ever produced the NotAllowedError it
// was waiting for. A plain `document.addEventListener('pointerdown', ...)` sidesteps the whole
// problem — it's a real native listener, synchronous with the actual touch, regardless of how
// many interop hops separate the *rest* of this file's own attempts from the gesture that caused
// them. Symptom this fixed, reported on a real device: no theme music/transition SFX at boot, mute
// toggles had no audible effect, starting a module produced no bgm — until directly interacting
// with an audio_track's own controls happened to coincide with the graph finally unmuting.
const pendingPlays = new Set();
let unlockListenerAttached = false;

function safePlay(audio) {
    const result = audio.play();
    if (result && typeof result.catch === 'function') {
        result.catch(err => {
            logAudio('play() rejected:', err?.name, err?.message, 'src =', audio.src);
            if (err && err.name === 'NotAllowedError') {
                pendingPlays.add(audio);
                attachUnlockListener();
            }
            // Other rejections (e.g. AbortError from a rapid pause/play race elsewhere in this
            // file) are expected transient conditions, not something to recover from here.
        });
    }
}

// Call whenever an element is intentionally stopped/paused/disposed, so a stale blocked-play
// request doesn't unexpectedly resume something the player no longer wants playing once they
// finally do interact with the page.
function cancelPendingPlay(audio) {
    pendingPlays.delete(audio);
}

function attachUnlockListener() {
    if (unlockListenerAttached) {
        return;
    }
    unlockListenerAttached = true;

    const unlock = () => {
        unlockListenerAttached = false;
        if (audioCtx && audioCtx.state === 'suspended') {
            logAudio('unlock() resuming suspended audioCtx on a real gesture');
            audioCtx.resume();
        }
        const toRetry = [...pendingPlays];
        pendingPlays.clear();
        if (toRetry.length > 0) {
            logAudio('unlock() retrying', toRetry.length, 'pending play(s):', toRetry.map(a => a.src));
        }
        for (const audio of toRetry) {
            audio.play().catch(() => {}); // best-effort — a real gesture just occurred
        }
        // Re-arm immediately — a still-suspended context (e.g. this same gesture's resume() call
        // hasn't resolved yet, or a later platform quirk needs a second try) shouldn't leave the
        // page with no listener at all for the next tap.
        attachUnlockListener();
    };
    document.addEventListener('pointerdown', unlock, { once: true });
    document.addEventListener('keydown', unlock, { once: true });
}

// Unconditional — see this block's own remarks above for why reactive-only attachment (on a
// caught play() rejection) never even engages on Android.
attachUnlockListener();

// Logs the underlying <audio> element's own native lifecycle events — not things our own code
// triggers, but things the *browser itself* can do to a background-music element unprompted
// (silently pausing/stalling a backgrounded tab's media, discarding its buffer, restarting the
// underlying network request). Attached to every bgm-role element (loop or playlist) so a
// browser-driven interruption shows up in the trail even though nothing in this file called it.
function logMediaLifecycle(audio, label) {
    for (const evt of ['pause', 'play', 'stalled', 'suspend', 'emptied', 'ended', 'error', 'seeked', 'waiting']) {
        audio.addEventListener(evt, () => logAudio(
            label, 'native event:', evt,
            'currentTime =', audio.currentTime.toFixed(2),
            'paused =', audio.paused,
            // networkState: 0=EMPTY, 1=IDLE, 2=LOADING, 3=NO_SOURCE. readyState: 0=NOTHING,
            // 1=METADATA, 2=CURRENT_DATA, 3=FUTURE_DATA, 4=ENOUGH_DATA — a stall stuck at
            // networkState 2 / readyState 0-1 points at the network fetch itself never actually
            // delivering bytes, as opposed to a decode/playback-side problem.
            'networkState =', audio.networkState,
            'readyState =', audio.readyState,
            'error =', audio.error ? `${audio.error.code}:${audio.error.message}` : null,
        ));
    }
}

function makeLoopingSource(ctx, url) {
    const audio = new Audio(url);
    audio.loop = true;
    logMediaLifecycle(audio, `bgm[${url}]`);
    // Skipped (not started) if the app is currently backgrounded — a passage/module transition
    // that resolves new bgm while backgrounded (unlikely, since JS is typically throttled/frozen
    // then, but not impossible) shouldn't sneak audible playback past the pause. mediaResume below
    // is what actually starts it once the app comes back.
    if (!backgroundPaused) {
        safePlay(audio);
    }
    const node = ctx.createMediaElementSource(audio);
    return {
        url, node,
        stop: () => { cancelPendingPlay(audio); audio.pause(); },
        cancelPending: () => cancelPendingPlay(audio),
        mediaPause: () => audio.pause(),
        mediaResume: () => safePlay(audio),
    };
}

function startBgmSource({ url, node, stop, cancelPending, mediaPause, mediaResume }, crossfadeSeconds) {
    const ctx = ensureContext();
    const gain = ctx.createGain();
    gain.gain.setValueAtTime(0, ctx.currentTime);
    gain.connect(ctx.destination);
    node.connect(gain);
    gain.gain.linearRampToValueAtTime(targetGain(), ctx.currentTime + crossfadeSeconds);

    const previous = currentBgm;
    currentBgm = { url, gain, stop, cancelPending, mediaPause, mediaResume };
    logAudio('startBgmSource', url, 'crossfadeSeconds =', crossfadeSeconds, 'supersedes =', previous?.url ?? null);

    if (previous) {
        // Cancelled immediately, not deferred to the stop() below — closes a real race: if
        // `previous`'s own play() was still blocked/pending (autoplay-unlock queue) when it got
        // superseded here, a document-wide click landing in the crossfade-out window would
        // otherwise retry and briefly resume it, audibly overlapping the new track until the
        // deferred stop() below finally paused it a moment later.
        previous.cancelPending?.();
        previous.gain.gain.linearRampToValueAtTime(0, ctx.currentTime + crossfadeSeconds);
        setTimeout(() => previous.stop(), crossfadeSeconds * 1000 + 100);
    }
}

export function playBgm(url, crossfadeSeconds = 1.5) {
    logAudio('playBgm called:', url, 'currentBgm.url =', currentBgm?.url ?? null, 'currentPlaylist =', currentPlaylist !== null);

    // Redundant calls for the exact track already playing (or already crossfading in) are a
    // no-op — without this, any caller re-announcing the same music (a stray re-render, two
    // navigation handlers racing each other, theme music being re-synced on return to the shell
    // when it was already playing) would restart the track from position 0 and re-trigger a full
    // crossfade for no audible reason. Only applies outside playlist mode — a single-track
    // override with the same URL as the currently-playing playlist entry is still a real mode
    // change (stop advancing, pin to this one track), not a no-op.
    if (currentPlaylist === null && currentBgm && currentBgm.url === url) {
        logAudio('playBgm: no-op, already current');
        return;
    }

    currentPlaylist = null;
    startBgmSource(makeLoopingSource(ensureContext(), url), crossfadeSeconds);
}

export function playBgmPlaylist(urls, order = 'sequence', crossfadeSeconds = 1.5) {
    logAudio('playBgmPlaylist called:', urls, order);
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
    const url = playlist.queue[playlist.index];
    const audio = new Audio(url);
    // Deliberately non-looping — this is what lets `ended` fire at all and drive auto-advance.
    // playBgm's single-track path is the opposite: it loops, so `ended` never fires there.
    audio.loop = false;
    logMediaLifecycle(audio, `playlist[${url}]`);
    audio.addEventListener('ended', () => {
        if (currentPlaylist !== playlist) {
            return; // a newer resolution winner has already superseded this playlist
        }
        advancePlaylist(playlist);
    });
    // See makeLoopingSource's own identical guard — don't sneak audible playback past an active
    // background pause.
    if (!backgroundPaused) {
        safePlay(audio);
    }
    const node = ctx.createMediaElementSource(audio);
    startBgmSource({
        url, node,
        stop: () => { cancelPendingPlay(audio); audio.pause(); },
        cancelPending: () => cancelPendingPlay(audio),
        mediaPause: () => audio.pause(),
        mediaResume: () => safePlay(audio),
    }, crossfadeSeconds);
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
    logAudio('stopBgm called, currentBgm.url =', currentBgm?.url ?? null);
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

// Called when the app itself loses foreground focus (native app-lifecycle events on MAUI heads,
// via AppLifecycleAudioBridge — see its own remarks; document.visibilitychange directly on the web
// build, below) — pauses the actual underlying <audio> element for whichever bgm is currently
// playing, not just its gain (silencing gain alone leaves the element decoding/using battery in
// the background, and doesn't stop it from being audible again the instant gain ramps back up
// mid-transition). One-shot SFX and audio_track (explicit player-initiated narration playback)
// are deliberately untouched — this is scoped to ambient background music only.
export function pauseBgmForBackground() {
    if (backgroundPaused) {
        return;
    }
    backgroundPaused = true;
    logAudio('pauseBgmForBackground, currentBgm.url =', currentBgm?.url ?? null);
    currentBgm?.mediaPause?.();
}

export function resumeBgmFromBackground() {
    if (!backgroundPaused) {
        return;
    }
    backgroundPaused = false;
    logAudio('resumeBgmFromBackground, currentBgm.url =', currentBgm?.url ?? null);
    // Covers both "was actually paused" and "was created while backgrounded and never started at
    // all" (makeLoopingSource/playCurrentPlaylistTrack's own backgroundPaused guard) — mediaResume
    // is safePlay() either way, idempotent if the element happens to already be playing.
    currentBgm?.mediaResume?.();
    if (audioCtx && audioCtx.state === 'suspended') {
        audioCtx.resume();
    }
}

export function playSfx(url) {
    const audio = new Audio(url);
    audio.volume = sfxGain();
    safePlay(audio);
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
    if (tracks.size === 0) {
        return;
    }

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
    const track = tracks.get(handle);
    if (track) {
        safePlay(track.audio);
    }
}

export function pauseTrack(handle) {
    const track = tracks.get(handle);
    if (track) {
        cancelPendingPlay(track.audio);
        track.audio.pause();
    }
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

    cancelPendingPlay(track.audio);
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
