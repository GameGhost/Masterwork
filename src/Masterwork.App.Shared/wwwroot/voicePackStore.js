// Own database, separate from the module and asset pack stores. One version of a voice pack is
// installed at a time, so everything is keyed by the pack id alone. Same split as assetPackStore.js:
// a small metadata record per pack, and one row per audio file so a pack is never held in memory whole.
const DB_NAME = "masterwork-voice-packs";
const DB_VERSION = 1;
const META_STORE = "voicePackMeta";
const ASSET_STORE = "voicePackAssets";
const ASSET_BY_PACK_INDEX = "byPack";

function openDb() {
    return new Promise((resolve, reject) => {
        const req = indexedDB.open(DB_NAME, DB_VERSION);
        req.onupgradeneeded = () => {
            const db = req.result;
            if (!db.objectStoreNames.contains(META_STORE)) {
                db.createObjectStore(META_STORE, { keyPath: "id" });
            }

            if (!db.objectStoreNames.contains(ASSET_STORE)) {
                const assetStore = db.createObjectStore(ASSET_STORE, { keyPath: ["id", "assetPath"] });
                assetStore.createIndex(ASSET_BY_PACK_INDEX, "id");
            }
        };
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

export async function putVoicePackMeta(meta) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readwrite");
        tx.objectStore(META_STORE).put(meta);
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
    });
}

export async function listVoicePackMeta() {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(META_STORE, "readonly");
        const req = tx.objectStore(META_STORE).getAll();
        req.onsuccess = () => resolve(req.result);
        req.onerror = () => reject(req.error);
    });
}

export async function putVoicePackAsset(id, assetPath, bytes) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readwrite");
        tx.objectStore(ASSET_STORE).put({ id, assetPath, bytes });
        tx.oncomplete = () => resolve();
        tx.onerror = () => reject(tx.error);
    });
}

function readAsset(db, id, assetPath) {
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const req = tx.objectStore(ASSET_STORE).get([id, assetPath]);
        req.onsuccess = () => resolve(req.result ? req.result.bytes : null);
        req.onerror = () => reject(req.error);
    });
}

export async function getVoicePackAsset(id, assetPath) {
    return readAsset(await openDb(), id, assetPath);
}

// The bytes become a Blob object URL here in JS, never crossing into .NET. The caller revokes it.
export async function getVoicePackAssetAsObjectUrl(id, assetPath, mimeType) {
    const bytes = await readAsset(await openDb(), id, assetPath);
    return bytes ? URL.createObjectURL(new Blob([bytes], { type: mimeType })) : null;
}

export async function listVoicePackAssetPaths(id) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction(ASSET_STORE, "readonly");
        const paths = [];
        const cursorReq = tx.objectStore(ASSET_STORE).index(ASSET_BY_PACK_INDEX).openKeyCursor(IDBKeyRange.only(id));
        cursorReq.onsuccess = () => {
            const cursor = cursorReq.result;
            if (cursor) {
                paths.push(cursor.primaryKey[1]); // primaryKey is [id, assetPath]
                cursor.continue();
            } else {
                resolve(paths);
            }
        };
        cursorReq.onerror = () => reject(cursorReq.error);
    });
}

// Deletes a pack's metadata and every one of its files in one transaction.
export async function clearVoicePack(id) {
    const db = await openDb();
    return new Promise((resolve, reject) => {
        const tx = db.transaction([META_STORE, ASSET_STORE], "readwrite");
        tx.objectStore(META_STORE).delete(id);

        const assetStore = tx.objectStore(ASSET_STORE);
        const cursorReq = assetStore.index(ASSET_BY_PACK_INDEX).openKeyCursor(IDBKeyRange.only(id));
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
