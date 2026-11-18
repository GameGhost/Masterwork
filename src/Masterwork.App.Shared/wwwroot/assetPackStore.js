// Own database, separate from moduleStore.js's "masterwork-modules" — an asset pack is keyed by
// [id, version] together (multiple versions can be installed side by side), not by id alone the
// way a module is. Same two-store split moduleStore.js uses (text metadata vs. per-asset binary
// content), for the same reason: loading/installing shouldn't require holding a whole decompressed
// package in memory. The asset sub-store is written to at install time even though nothing reads it
// back yet, so a later asset-resolution feature doesn't need every already-installed pack reinstalled.
const DB_NAME = "masterwork-asset-packs";
const DB_VERSION = 1;
const META_STORE = "assetPackMeta";
const ASSET_STORE = "assetPackAssets";
const ASSET_BY_PACK_INDEX = "byPack";

function openDb() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains(META_STORE)) {
                db.createObjectStore(META_STORE, { keyPath: ["id", "version"] });
            }

            if (!db.objectStoreNames.contains(ASSET_STORE)) {
                const assetStore = db.createObjectStore(ASSET_STORE, { keyPath: ["id", "version", "assetPath"] });
                assetStore.createIndex(ASSET_BY_PACK_INDEX, ["id", "version"]);
            }
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

export async function putAssetPackMeta(meta) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readwrite");
        tx.objectStore(META_STORE).put(meta);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
    });
}

export async function listAssetPackMeta() {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readonly");
        const req = tx.objectStore(META_STORE).getAll();
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

export async function getAssetPackMeta(id, version) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readonly");
        const req = tx.objectStore(META_STORE).get([id, version]);
        req.onsuccess = () => resolve(req.result ?? null);
        req.onerror = () => reject(req.error);
    });
}

export async function putAssetPackAsset(id, version, assetPath, bytes) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readwrite");
        tx.objectStore(ASSET_STORE).put({ id, version, assetPath, bytes });
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
    });
}

// Reads one asset's raw bytes back into .NET — used only for content that needs to be read as data
// (style.css, injected as literal text), never for display assets (images/fonts), which should go
// through getAssetPackAssetAsObjectUrl below instead. Mirrors moduleStore.js's getModuleAsset.
export async function getAssetPackAsset(id, version, assetPath) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const req = tx.objectStore(ASSET_STORE).get([id, version, assetPath]);
        req.onsuccess = () => resolve(req.result ? req.result.bytes : null);
        req.onerror = () => reject(req.error);
    });
}

// Reads one asset's bytes and turns them into a Blob object URL entirely here in JS — same
// bytes-never-cross-into-.NET reasoning as moduleStore.js's getModuleAssetAsObjectUrl. The caller is
// responsible for revoking the URL once it's no longer needed.
export async function getAssetPackAssetAsObjectUrl(id, version, assetPath, mimeType) {
    const db = await openDb();
    const bytes = await new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const req = tx.objectStore(ASSET_STORE).get([id, version, assetPath]);
        req.onsuccess = () => resolve(req.result ? req.result.bytes : null);
        req.onerror = () => reject(req.error);
    });

    if (!bytes) {
        return null;
    }

    const blob = new Blob([bytes], { type: mimeType });
    return URL.createObjectURL(blob);
}

// Lists one pack's asset paths only (key-only cursor, never reads bytes) — mirrors
// moduleStore.js's listModuleAssetPaths.
export async function listAssetPackAssetPaths(id, version) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const index = tx.objectStore(ASSET_STORE).index(ASSET_BY_PACK_INDEX);
        const paths = [];
        const cursorReq = index.openKeyCursor(IDBKeyRange.only([id, version]));
        cursorReq.onsuccess = () => {
            const cursor = cursorReq.result;
            if (cursor) {
                paths.push(cursor.primaryKey[2]); // primaryKey is [id, version, assetPath]
                cursor.continue();
            } else {
                resolve(paths);
            }
        };
        cursorReq.onerror = () => reject(cursorReq.error);
    });
}

// Deletes a pack's metadata record and every one of its asset rows as a single transaction — same
// reasoning as moduleStore.js's clearModule.
export async function clearAssetPack(id, version) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction([META_STORE, ASSET_STORE], "readwrite");
        tx.objectStore(META_STORE).delete([id, version]);

        const assetStore = tx.objectStore(ASSET_STORE);
        const index = assetStore.index(ASSET_BY_PACK_INDEX);
        const cursorReq = index.openKeyCursor(IDBKeyRange.only([id, version]));
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
