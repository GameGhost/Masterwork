// Native, hardware-backed SHA256 via the browser's Web Crypto API. .NET's own IncrementalHash (used
// as a fallback below) turned out to be dramatically slower under Blazor WASM's interpreter — a
// ~37MB file that crypto.subtle.digest hashes in well under a second took upward of 45 seconds via
// chunked IncrementalHash.AppendData calls, confirmed by measurement (a single 'setTimeout' handler
// clocking 48974ms in the browser's own performance violation log). crypto.subtle.digest requires a
// secure context (HTTPS or localhost); ModuleHasher falls back to the managed implementation if this
// throws (e.g. an insecure deployment).
export async function sha256Hex(bytes) {
    const digest = await crypto.subtle.digest("SHA-256", bytes);
    const view = new Uint8Array(digest);
    let hex = "";
    for (let i = 0; i < view.length; i++) {
        hex += view[i].toString(16).padStart(2, "0");
    }
    return hex;
}
