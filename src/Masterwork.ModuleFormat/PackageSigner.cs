using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace Masterwork.ModuleFormat;

/// <summary>Result of <see cref="PackageSigner.Verify"/> — a fact about the package's embedded signature, not a trust decision (that's a caller-side policy, e.g. comparing <see cref="CertificateThumbprint"/> against a pinned anchor or a "trusted publishers" list).</summary>
public enum PackageVerificationOutcome
{
    /// <summary>No <c>signature.sig</c> entry present at all.</summary>
    Unsigned,

    /// <summary>A signature is present and verifies against its own embedded certificate.</summary>
    Valid,

    /// <summary><c>signature.sig</c> is present but malformed, or the signature doesn't verify against the package's actual content (tampered, corrupted, or wrong key).</summary>
    Invalid,
}

/// <summary>
/// <see cref="PackageVerificationOutcome.Valid"/> carries <see cref="CertificateSubject"/>/
/// <see cref="CertificateThumbprint"/> so a caller can render the trust-tier UI (compare the
/// thumbprint against a pinned anchor or a remembered "trust this publisher" choice, or show the
/// subject/thumbprint in an unrecognized-signer warning) without re-parsing the package itself.
/// Both are <see langword="null"/> for <see cref="PackageVerificationOutcome.Unsigned"/> and
/// <see cref="PackageVerificationOutcome.Invalid"/> (an invalid signature's claimed certificate
/// isn't trustworthy information — reading a corrupted/hostile signature.sig enough to identify
/// *whose* signature failed isn't attempted).
/// </summary>
public sealed record PackageVerificationResult(
    PackageVerificationOutcome Outcome,
    string? CertificateSubject = null,
    string? CertificateThumbprint = null
);

/// <summary>
/// Signs and verifies a <c>.mwm</c>/<c>.mwassets</c> package (or any zip — this operates on raw zip
/// bytes, with no MWS-specific knowledge, so it needs no changes to <see cref="ModulePackage"/>/
/// <see cref="AssetPackPackage"/> to work with either package kind) via one embedded
/// <c>signature.sig</c> entry. Deliberately minimal for what's actually being verified — see
/// <c>phase6-design.md</c> §3 in the design repo: MWS content is sandboxed declarative data, not
/// executable code, so this skips certificate-chain validation, CRL/OCSP checking, and expiry
/// checking entirely. A self-signed certificate has no chain to validate in the first place;
/// rotation (a new certificate, re-pinned by an app update) is the only "revocation" that exists for
/// one. <see cref="Verify"/> reports facts (signed/unsigned/valid/invalid, and the signer's
/// identity) — deciding whether that identity is *trusted* is entirely the caller's policy.
/// </summary>
public static class PackageSigner
{
    private const string SignatureEntryPath = "signature.sig";

    private sealed record SignatureFile(string Algorithm, string Certificate, string Signature);

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
        using var rsa = signingCertificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Signing certificate has no RSA private key — load it from a .pfx that includes one.");

        var entries = ReadEntries(zipBytes, excludeSignature: true);
        var digest = ComputeDigest(entries);
        var signatureBytes = rsa.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var signatureFile = new SignatureFile(
            Algorithm: "RS256",
            Certificate: Convert.ToBase64String(signingCertificate.Export(X509ContentType.Cert)),
            Signature: Convert.ToBase64String(signatureBytes));
        var signatureJson = JsonSerializer.SerializeToUtf8Bytes(signatureFile);

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

    /// <summary>Verifies a package's embedded signature, if any — see <see cref="PackageVerificationResult"/>.</summary>
    public static PackageVerificationResult Verify(byte[] zipBytes)
    {
        var allEntries = ReadEntries(zipBytes, excludeSignature: false);
        var sigEntry = allEntries.FirstOrDefault(e => e.Path.Equals(SignatureEntryPath, StringComparison.OrdinalIgnoreCase));
        if (sigEntry.Path is null)
        {
            return new PackageVerificationResult(PackageVerificationOutcome.Unsigned);
        }

        SignatureFile? signatureFile;
        try
        {
            signatureFile = JsonSerializer.Deserialize<SignatureFile>(sigEntry.Bytes);
        }
        catch (JsonException)
        {
            return new PackageVerificationResult(PackageVerificationOutcome.Invalid);
        }

        if (signatureFile is not { Algorithm: "RS256" })
        {
            return new PackageVerificationResult(PackageVerificationOutcome.Invalid);
        }

        X509Certificate2 cert;
        byte[] signatureBytes;
        try
        {
            cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(signatureFile.Certificate));
            signatureBytes = Convert.FromBase64String(signatureFile.Signature);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return new PackageVerificationResult(PackageVerificationOutcome.Invalid);
        }

        using var rsa = cert.GetRSAPublicKey();
        if (rsa is null)
        {
            return new PackageVerificationResult(PackageVerificationOutcome.Invalid);
        }

        var entriesExcludingSignature = allEntries.Where(e => !e.Path.Equals(SignatureEntryPath, StringComparison.OrdinalIgnoreCase)).ToList();
        var digest = ComputeDigest(entriesExcludingSignature);

        var valid = rsa.VerifyHash(digest, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (!valid)
        {
            return new PackageVerificationResult(PackageVerificationOutcome.Invalid);
        }

        return new PackageVerificationResult(PackageVerificationOutcome.Valid, cert.Subject, cert.GetCertHashString(HashAlgorithmName.SHA256));
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
