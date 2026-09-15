using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Signs and verifies a <c>.mwm</c>/<c>.mwassets</c> package (or any zip — this operates on raw zip
/// bytes, with no MWS-specific knowledge, so it needs no changes to <see cref="ModulePackage"/>/
/// <see cref="AssetPackPackage"/> to work with either package kind) via one embedded
/// <c>signature.sig</c> entry. Deliberately minimal for what's actually being verified: MWS content
/// is sandboxed declarative data, not executable code, so this skips certificate-chain validation,
/// CRL/OCSP checking, and expiry checking entirely. A self-signed certificate has no chain to
/// validate in the first place, and rotation (a new certificate, re-pinned by an app update) is the
/// only "revocation" that exists for one. <see cref="Verify"/> reports facts (signed/unsigned/
/// valid/invalid, and the signer's identity) — deciding whether that identity is *trusted* is
/// entirely the caller's policy.
/// </summary>
public static class PackageSigner
{
    private const string SignatureEntryPath = "signature.sig";

    /// <summary>
    /// Returns a copy of <paramref name="zipBytes"/> with a <c>signature.sig</c> entry added (or
    /// replaced, if one was already present — re-signing drops the old signature rather than
    /// stacking them). The certificate's private key signs a SHA-256 digest computed over every
    /// *other* entry (see <see cref="ComputeDigest"/>) — the signature itself is never part of what
    /// it covers.
    /// </summary>
    /// <param name="signingCertificate">Must have an RSA private key attached (e.g. loaded from a <c>.pfx</c>).</param>
    public static byte[] Sign(byte[] zipBytes, X509Certificate2 signingCertificate)
    {
        var entries = ReadEntries(zipBytes, excludeSignature: true);
        var signatureJson = SignatureEnvelope.Build(ComputeDigest(entries), signingCertificate);

        using var outputStream = new MemoryStream();
        using (var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var entryStream = entry.Open();
                entryStream.Write(bytes);
            }

            var sigEntry = archive.CreateEntry(SignatureEntryPath);
            using var sigStream = sigEntry.Open();
            sigStream.Write(signatureJson);
        }

        return outputStream.ToArray();
    }

    /// <summary>Verifies a package's embedded signature, if any — see <see cref="SignatureVerificationResult"/>.</summary>
    public static SignatureVerificationResult Verify(byte[] zipBytes)
    {
        // Looked up by name before anything else — an unsigned package (every package built before
        // signing existed) then costs one directory lookup instead of decompressing the whole
        // archive only to discover there was nothing to check.
        byte[] signatureEntryBytes;
        using (var probeStream = new MemoryStream(zipBytes))
        using (var probeArchive = new ZipArchive(probeStream, ZipArchiveMode.Read))
        {
            var probeEntry = probeArchive.GetEntry(SignatureEntryPath);
            if (probeEntry is null)
            {
                return new SignatureVerificationResult(SignatureVerificationOutcome.Unsigned);
            }

            using var probeEntryStream = probeEntry.Open();
            using var probeMemory = new MemoryStream();
            probeEntryStream.CopyTo(probeMemory);
            signatureEntryBytes = probeMemory.ToArray();
        }

        var digest = ComputeDigest(ReadEntries(zipBytes, excludeSignature: true));
        return SignatureEnvelope.VerifyDigest(digest, signatureEntryBytes);
    }

    // Order-independent (sorted by path) so re-zipping the same logical content in a different entry
    // order still signs/verifies identically — a zip's own entry order isn't guaranteed stable
    // across different writers/tools. Each entry's path is hashed alongside its bytes (path, then a
    // 0x00 separator that can't appear in a zip entry path, then the bytes) so swapping two
    // same-content files' names can't slip past verification unnoticed.
    private static byte[] ComputeDigest(IReadOnlyList<(string Path, byte[] Bytes)> entries)
    {
        using var sha256 = SHA256.Create();
        using var stream = new CryptoStream(Stream.Null, sha256, CryptoStreamMode.Write);
        foreach (var (path, bytes) in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            stream.Write(Encoding.UTF8.GetBytes(path));
            stream.WriteByte(0);
            stream.Write(bytes);
        }
        stream.FlushFinalBlock();
        return sha256.Hash!;
    }

    private static List<(string Path, byte[] Bytes)> ReadEntries(byte[] zipBytes, bool excludeSignature)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entries = new List<(string, byte[])>();
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // directory entry
            }

            var path = entry.FullName.Replace('\\', '/');
            if (excludeSignature && path.Equals(SignatureEntryPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            entries.Add((path, memory.ToArray()));
        }

        return entries;
    }
}
