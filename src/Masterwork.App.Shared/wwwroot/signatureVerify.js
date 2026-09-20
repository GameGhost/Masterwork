// Signature verification via the browser's Web Crypto API.
//
// The browser runtime has no System.Security.Cryptography.X509Certificates at all — every entry
// point there throws PlatformNotSupportedException — so verification on the web head has to happen
// out here. What's needed from the certificate is its RSA public key, its SHA-256 thumbprint, and
// its Common Name.
//
// The public key is parsed out of the certificate rather than taken from a separate field, and that
// is deliberate: the trust decision compares the certificate's thumbprint against a pinned value, so
// if the verifying key could come from anywhere else, a forged signature could be paired with a
// trusted certificate and pass. The key and the thumbprint must describe the same bytes.

// Reads one DER tag-length-value header at `offset`.
function readTlv(bytes, offset) {
    const tag = bytes[offset];
    let cursor = offset + 1;
    let length = bytes[cursor++];

    if (length & 0x80) {
        const lengthBytes = length & 0x7f;
        // Indefinite-length (0x80) is not valid DER, and a length field this long means the input
        // isn't a certificate we can make sense of.
        if (lengthBytes === 0 || lengthBytes > 4) {
            throw new Error("unsupported DER length");
        }
        length = 0;
        for (let i = 0; i < lengthBytes; i++) {
            length = (length << 8) | bytes[cursor++];
        }
    }

    const end = cursor + length;
    if (end > bytes.length) {
        throw new Error("DER length runs past the end of the input");
    }

    return { tag, contentStart: cursor, end };
}

// Certificate ::= SEQUENCE { tbsCertificate, signatureAlgorithm, signatureValue }
// TBSCertificate ::= SEQUENCE { [0] version DEFAULT v1, serialNumber, signature, issuer,
//                               validity, subject, subjectPublicKeyInfo, ... }
function parseCertificate(der) {
    const certificate = readTlv(der, 0);
    const tbs = readTlv(der, certificate.contentStart);

    let cursor = tbs.contentStart;
    // The version tag is [0] EXPLICIT and optional; everything after it shifts by one when absent.
    if (der[cursor] === 0xa0) {
        cursor = readTlv(der, cursor).end;
    }

    const fields = [];
    while (cursor < tbs.end && fields.length < 6) {
        const field = readTlv(der, cursor);
        fields.push({ start: cursor, ...field });
        cursor = field.end;
    }

    // serialNumber, signature, issuer, validity, subject, subjectPublicKeyInfo
    if (fields.length < 6) {
        throw new Error("certificate is missing expected fields");
    }

    return { subject: fields[4], publicKeyInfo: fields[5] };
}

// CN is OID 2.5.4.3, which encodes as 06 03 55 04 03. The subject is a sequence of relative
// distinguished names; this finds the first CN attribute and reads the string that follows it.
function readCommonName(der, subject) {
    const oid = [0x06, 0x03, 0x55, 0x04, 0x03];

    for (let i = subject.start; i < subject.end - oid.length; i++) {
        let matched = true;
        for (let j = 0; j < oid.length; j++) {
            if (der[i + j] !== oid[j]) {
                matched = false;
                break;
            }
        }

        if (!matched) {
            continue;
        }

        const value = readTlv(der, i + oid.length);
        return new TextDecoder().decode(der.subarray(value.contentStart, value.end));
    }

    return null;
}

function toHex(buffer) {
    const view = new Uint8Array(buffer);
    let hex = "";
    for (let i = 0; i < view.length; i++) {
        hex += view[i].toString(16).padStart(2, "0");
    }
    return hex;
}

function fromBase64(text) {
    const binary = atob(text);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes;
}

/**
 * Verifies a signature envelope against a digest.
 *
 * `digest` is the 32 bytes the signature covers — the envelope signs it as a message, so this is
 * all Web Crypto needs regardless of how much content produced it. Returns the same shape
 * SignatureVerificationResult has on the managed side.
 */
export async function verifyDigest(digestBytes, certificateBase64, signatureBase64) {
    let der;
    let parsed;
    try {
        der = fromBase64(certificateBase64);
        parsed = parseCertificate(der);
    } catch {
        return { valid: false };
    }

    const spki = der.subarray(parsed.publicKeyInfo.start, parsed.publicKeyInfo.end);

    let key;
    try {
        key = await crypto.subtle.importKey(
            "spki",
            spki,
            { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
            false,
            ["verify"]);
    } catch {
        return { valid: false };
    }

    let signature;
    try {
        signature = fromBase64(signatureBase64);
    } catch {
        return { valid: false };
    }

    const valid = await crypto.subtle.verify(
        { name: "RSASSA-PKCS1-v1_5" },
        key,
        signature,
        new Uint8Array(digestBytes));

    if (!valid) {
        return { valid: false };
    }

    // The thumbprint identifies the certificate as a whole, so it hashes the DER exactly as
    // X509Certificate2.GetCertHashString(SHA256) does on the managed side.
    return {
        valid: true,
        thumbprint: toHex(await crypto.subtle.digest("SHA-256", der)).toUpperCase(),
        commonName: readCommonName(der, parsed.subject),
    };
}
