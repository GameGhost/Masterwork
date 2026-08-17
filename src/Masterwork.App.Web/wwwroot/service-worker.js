// Network-first, falling back to cache only when the network fetch fails (offline). Not every
// static asset this app serves is content-hashed — Razor Class Library assets referenced by a
// fixed path (app.css, and every custom JS module loaded via IJSRuntime's plain string imports:
// audio.js, moduleStore.js, appSettings.js, saveExport.js) keep the same URL across a rebuild, so
// a cache-first strategy (the previous "stale-while-revalidate") could keep serving a stale copy
// of those indefinitely until a second reload's background revalidation caught up — a real "why
// isn't my change showing up" trap during development. Network-first still serves the cache when
// genuinely offline (the whole point of this file), it just no longer wins a race against a live
// network that's already returned fresher content.
const CACHE_NAME = "masterwork-cache-v3";
const PRECACHE_URLS = ["/", "/manifest.json"];

self.addEventListener("install", (event) => {
    event.waitUntil(
        caches.open(CACHE_NAME).then((cache) => cache.addAll(PRECACHE_URLS))
    );
    self.skipWaiting();
});

self.addEventListener("activate", (event) => {
    event.waitUntil(
        caches.keys().then((keys) =>
            Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key)))
        )
    );
    self.clients.claim();
});

self.addEventListener("fetch", (event) => {
    if (event.request.method !== "GET") {
        return;
    }

    // Never intercept Range requests at all — not just "don't cache the 206" (the previous fix,
    // v2), but don't reconstruct the response through this handler's own fetch()+respondWith()
    // wrapper in the first place. This app streams real audio (bgm loops, audio_track) through
    // here, and the browser's own <audio>/<video> pipeline depends on precise byte-range semantics
    // (Content-Range, partial-response resumption, connection reuse) that a hand-rolled
    // respondWith() wrapper doesn't reliably preserve even when it forwards the same request/
    // response — a real, well-documented class of "service worker breaks media streaming" bug.
    // Root-caused via a player report + a captured console trail: main-menu-theme.ogg's <audio>
    // element stuck in a perpetual `waiting` -> `seeked(currentTime=0)` cycle every ~4.5s, with
    // nothing in this app's own JS/C# calling playBgm() again in between — i.e. the browser itself
    // could never get past a stalled fetch for this resource. No respondWith() here at all means
    // the browser handles the request exactly as if no service worker were registered.
    if (event.request.headers.has("range")) {
        return;
    }

    event.respondWith(
        fetch(event.request)
            .then((response) => {
                // response.ok is true for any 2xx status, including 206 Partial Content — which
                // the Cache API always rejects (cache.put() throws "Partial response is
                // unsupported"). Shouldn't occur now that Range requests are skipped above
                // entirely, but checked defensively in case some non-Range request ever comes
                // back partial.
                if (response.ok && response.status !== 206) {
                    const clone = response.clone();
                    caches.open(CACHE_NAME).then((cache) => cache.put(event.request, clone));
                }
                return response;
            })
            .catch(() => caches.match(event.request))
    );
});
