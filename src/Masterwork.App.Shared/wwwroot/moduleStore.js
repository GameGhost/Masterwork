// Schema v2: the whole-package "modules" store (v1) is superseded by two stores that let a
// module's text metadata and its (often much larger) per-asset binary content be read/written
// independently, so loading/installing a module no longer means holding the entire decompressed
// package in memory or blocking on one giant IndexedDB read/write. The old "modules" store from v1
// databases is left in place untouched — orphaned, never read or written again. There is no
// migration: any module installed under the old schema is simply invisible to the app now and has
// to be re-uploaded.
const DB_NAME = "masterwork-modules";
const DB_VERSION = 2;
const META_STORE = "moduleMeta";
const ASSET_STORE = "moduleAssets";
const ASSET_BY_MODULE_INDEX = "byModule";

function openDb() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains(META_STORE)) {
                db.createObjectStore(META_STORE, { keyPath: "id" });
            }

            if (!db.objectStoreNames.contains(ASSET_STORE)) {
                const assetStore = db.createObjectStore(ASSET_STORE, { keyPath: ["moduleId", "assetPath"] });
                assetStore.createIndex(ASSET_BY_MODULE_INDEX, "moduleId");
            }
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

export async function putModuleMeta(meta) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readwrite");
        tx.objectStore(META_STORE).put(meta);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
    });
}

// Deliberately returns only the fields the module-select carousel/list actually needs, not the
// full record getModuleMeta(id) does -- getAll() itself reads every row's full object cheaply
// in-process, but the *next* step (JSInterop marshaling the result across to .NET as JSON) is not
// cheap, and a module's passages/restext/layouts (its entire playable text) can be multiple MB per
// module. Stripping them here, before that marshal, is the same principle
// getModuleAssetAsObjectUrl already applies to asset bytes (never cross the boundary if the caller
// doesn't need the bytes on this side) -- this was the actual cause of multi-second stalls just
// opening Start New Game, not anything about resolving thumbnails.
export async function listModuleMeta() {
    const db = await openDb();
    const rows = await new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readonly");
        const req = tx.objectStore(META_STORE).getAll();
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });

    return rows.map(m => ({
        id: m.id,
        title: m.title,
        version: m.version,
        description: m.description,
        languages: m.languages,
        sha256: m.sha256,
        manifestYaml: m.manifestYaml,
    }));
}

export async function getModuleMeta(id) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readonly");
        const req = tx.objectStore(META_STORE).get(id);
        req.onsuccess = () => resolve(req.result ?? null);
        req.onerror = () => reject(req.error);
    });
}

export async function putModuleAsset(moduleId, assetPath, bytes) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readwrite");
        tx.objectStore(ASSET_STORE).put({ moduleId, assetPath, bytes });
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
    });
}

// Reads one asset's raw bytes back into .NET — used only for content that needs to be read as data
// (style.css, injected as literal text), never for display assets (images/fonts), which should go
// through getModuleAssetAsObjectUrl below instead to avoid marshaling their bytes into .NET at all.
export async function getModuleAsset(moduleId, assetPath) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const req = tx.objectStore(ASSET_STORE).get([moduleId, assetPath]);
        req.onsuccess = () => resolve(req.result ? req.result.bytes : null);
        req.onerror = () => reject(req.error);
    });
}

// Reads one asset's bytes and turns them into a Blob object URL entirely here in JS — the bytes
// themselves never cross into .NET, only the short resulting URL string does. This is the whole
// point: marshaling a large byte[] back into managed memory per asset (the previous design) was
// measured as the dominant cost behind multi-second stalls and large memory spikes when preloading a
// module with many sizable images. The caller (PreloadedModuleAssetSource) is responsible for
// revoking the URL via revokeObjectUrl once the module unloads.
export async function getModuleAssetAsObjectUrl(moduleId, assetPath, mimeType) {
    const db = await openDb();
    const bytes = await new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const req = tx.objectStore(ASSET_STORE).get([moduleId, assetPath]);
        req.onsuccess = () => resolve(req.result ? req.result.bytes : null);
        req.onerror = () => reject(req.error);
    });

    if (!bytes) {
        return null;
    }

    const blob = new Blob([bytes], { type: mimeType });
    return URL.createObjectURL(blob);
}

export function revokeObjectUrl(url) {
    URL.revokeObjectURL(url);
}

// Lists a module's asset paths only (key-only cursor — never reads the (often large) bytes of any
// row), so PreloadedModuleAssetSource can learn the full asset set and a progress total up front
// before fetching any actual bytes.
export async function listModuleAssetPaths(moduleId) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const index = tx.objectStore(ASSET_STORE).index(ASSET_BY_MODULE_INDEX);
        const paths = [];
        const cursorReq = index.openKeyCursor(IDBKeyRange.only(moduleId));
        cursorReq.onsuccess = () => {
            const cursor = cursorReq.result;
            if (cursor) {
                paths.push(cursor.primaryKey[1]); // primaryKey is [moduleId, assetPath]
                cursor.continue();
            } else {
                resolve(paths);
            }
        };
        cursorReq.onerror = () => reject(cursorReq.error);
    });
}

// Deletes a module's metadata record and every one of its asset rows (looked up via the byModule
// index) as a single transaction, so a delete never leaves orphaned asset rows behind on failure.
export async function clearModule(id) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction([META_STORE, ASSET_STORE], "readwrite");
        tx.objectStore(META_STORE).delete(id);

        const assetStore = tx.objectStore(ASSET_STORE);
        const index = assetStore.index(ASSET_BY_MODULE_INDEX);
        const cursorReq = index.openKeyCursor(IDBKeyRange.only(id));
        cursorReq.onsuccess = () => {
            const cursor = cursorReq.result;
            if (cursor) {
                assetStore.delete(cursor.primaryKey);
                cursor.continue();
            }
        };

        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
    });
}
