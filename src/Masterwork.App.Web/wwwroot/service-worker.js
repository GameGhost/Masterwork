// Network-first, falling back to cache only when the network fetch fails (offline). Not every
// static asset this app serves is content-hashed — Razor Class Library assets referenced by a
// fixed path (app.css, and every custom JS module loaded via IJSRuntime's plain string imports:
// audio.js, moduleStore.js, appSettings.js, saveExport.js) keep the same URL across a rebuild, so
// a cache-first strategy (the previous "stale-while-revalidate") could keep serving a stale copy
// of those indefinitely until a second reload's background revalidation caught up — a real "why
// isn't my change showing up" trap during development. Network-first still serves the cache when
// genuinely offline (the whole point of this file), it just no longer wins a race against a live
// network that's already returned fresher content.
const CACHE_NAME = "masterwork-cache-v2";
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

    event.respondWith(
        fetch(event.request)
            .then((response) => {
                if (response.ok) {
                    const clone = response.clone();
                    caches.open(CACHE_NAME).then((cache) => cache.put(event.request, clone));
                }
                return response;
            })
            .catch(() => caches.match(event.request))
    );
});
