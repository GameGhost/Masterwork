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
    /// *other* entry (see <see cref="ComputeContentDigest"/>) — the signature itself is never part of what
    /// it covers.
    /// </summary>
    /// <param name="signingCertificate">Must have an RSA private key attached (e.g. loaded from a <c>.pfx</c>).</param>
    public static byte[] Sign(byte[] zipBytes, X509Certificate2 signingCertificate)
    {
        var signatureJson = SignatureEnvelope.Build(ComputeContentDigest(zipBytes), signingCertificate);

        using var outputStream = new MemoryStream();
        using (var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Re-read rather than reusing a materialized list: signing runs in the packing tool,
            // where a second pass costs less than holding the whole package decompressed.
            foreach (var (path, bytes) in EnumerateSignedEntries(zipBytes))
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

    /// <summary>
    /// Verifies a package's embedded signature, if any — see <see cref="SignatureVerificationResult"/>.
    /// </summary>
    /// <remarks>
    /// Reads certificates, so this is unavailable in the browser runtime. App code should call
    /// <c>SignatureVerifier.VerifyPackageAsync</c>, which works on every head.
    /// </remarks>
    public static SignatureVerificationResult Verify(byte[] zipBytes)
    {
        if (ReadSignatureEntry(zipBytes) is not { } signatureEntryBytes)
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Unsigned);
        }

        return SignatureEnvelope.VerifyDigest(ComputeContentDigest(zipBytes), signatureEntryBytes);
    }

    /// <summary>
    /// Returns the package's <c>signature.sig</c> entry, or <see langword="null"/> if it has none.
    /// Looked up by name before anything else, so an unsigned package — every package built before
    /// signing existed — costs one directory lookup instead of decompressing the whole archive only
    /// to discover there was nothing to check.
    /// </summary>
    public static byte[]? ReadSignatureEntry(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        if (archive.GetEntry(SignatureEntryPath) is not { } entry)
        {
            return null;
        }

        using var entryStream = entry.Open();
        using var memory = new MemoryStream();
        entryStream.CopyTo(memory);
        return memory.ToArray();
    }

    /// <summary>The digest a package's signature covers — every entry except the signature itself.</summary>
    public static byte[] ComputeContentDigest(byte[] zipBytes)
    {
        var entryHashes = new List<(string Path, byte[] Hash)>();
        foreach (var (path, bytes) in EnumerateSignedEntries(zipBytes))
        {
            entryHashes.Add((path, SHA256.HashData(BuildEntryPreimage(path, bytes))));
        }

        return CombineEntryHashes(entryHashes);
    }

    /// <summary>How many entries the signature covers. Reads the zip directory only, not the content.</summary>
    public static int CountSignedEntries(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        return archive.Entries.Count(entry =>
            !string.IsNullOrEmpty(entry.Name)
            && !entry.FullName.Replace('\\', '/').Equals(SignatureEntryPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Yields every entry the signature covers, one at a time, reading each only as it's produced.
    /// Callers that hash incrementally therefore hold one entry in memory rather than the whole
    /// decompressed package.
    /// </summary>
    public static IEnumerable<(string Path, byte[] Bytes)> EnumerateSignedEntries(byte[] zipBytes)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue; // directory entry
            }

            var path = entry.FullName.Replace('\\', '/');
            if (path.Equals(SignatureEntryPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var entryStream = entry.Open();
            using var memory = new MemoryStream();
            entryStream.CopyTo(memory);
            yield return (path, memory.ToArray());
        }
    }

    /// <summary>
    /// The bytes hashed for one entry: its path, a <c>0x00</c> separator that can't occur in a zip
    /// entry path, then its content. Binding the path to the content is what stops two same-content
    /// files having their names swapped unnoticed.
    /// </summary>
    public static byte[] BuildEntryPreimage(string path, byte[] bytes)
    {
        var pathBytes = Encoding.UTF8.GetBytes(path);
        var preimage = new byte[pathBytes.Length + 1 + bytes.Length];

        pathBytes.CopyTo(preimage, 0);
        preimage[pathBytes.Length] = 0;
        bytes.CopyTo(preimage, pathBytes.Length + 1);

        return preimage;
    }

    /// <summary>
    /// Combines per-entry hashes into the package digest: sorted by path, concatenated, hashed once.
    ///
    /// Two levels rather than one continuous hash over all content, because it lets a verifier hash
    /// each entry independently — which is what makes the browser's own crypto usable, since
    /// <c>crypto.subtle</c> is one-shot with no streaming API. Hashing the whole package as a single
    /// run would mean holding all of it in memory at once to hand over. Sorting by path keeps the
    /// result independent of the order a zip happens to store its entries in.
    /// </summary>
    public static byte[] CombineEntryHashes(IEnumerable<(string Path, byte[] Hash)> entryHashes)
    {
        using var combined = new MemoryStream();
        foreach (var (_, hash) in entryHashes.OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            combined.Write(hash);
        }

        return SHA256.HashData(combined.ToArray());
    }

}
