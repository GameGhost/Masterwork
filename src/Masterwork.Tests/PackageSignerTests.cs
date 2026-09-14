using System.IO.Compression;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class PackageSignerTests
{
    private static byte[] MakeZip(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    // Appends/replaces one entry in an existing zip's bytes. A MemoryStream constructed directly
    // from a byte[] is fixed-capacity (not expandable) even though it's writable, which
    // ZipArchiveMode.Update needs when a new/larger entry doesn't fit in the original size -- so
    // this copies into a fresh, growable MemoryStream first.
    private static byte[] AppendEntry(byte[] zipBytes, string path, byte[] entryBytes)
    {
        var stream = new MemoryStream();
        stream.Write(zipBytes);
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(path)?.Delete();
            var entry = archive.CreateEntry(path);
            using var entryStream = entry.Open();
            entryStream.Write(entryBytes);
        }

        return stream.ToArray();
    }

    private static byte[] ReadEntry(byte[] zipBytes, string path)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry(path)!;
        using var entryStream = entry.Open();
        using var memory = new MemoryStream();
        entryStream.CopyTo(memory);
        return memory.ToArray();
    }

    [Fact]
    public void Verify_UnsignedPackage_ReturnsUnsigned()
    {
        var zip = MakeZip(("manifest.yaml", "id: 'x'"));

        var result = PackageSigner.Verify(zip);

        Assert.Equal(PackageVerificationOutcome.Unsigned, result.Outcome);
        Assert.Null(result.CertificateSubject);
        Assert.Null(result.CertificateThumbprint);
        Assert.Null(result.CertificateCommonName);
    }

    [Fact]
    public void SignThenVerify_ValidSignature_ReportsCertificateIdentity()
    {
        var cert = SelfSignedCertificateGenerator.Create("Masterwork Test Signer");
        var zip = MakeZip(("manifest.yaml", "id: 'x'"), ("assets/style.css", "body { color: red; }"));

        var signed = PackageSigner.Sign(zip, cert);
        var result = PackageSigner.Verify(signed);

        Assert.Equal(PackageVerificationOutcome.Valid, result.Outcome);
        Assert.Equal(cert.Subject, result.CertificateSubject);
        Assert.Equal(cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), result.CertificateThumbprint);

        // The player-facing name is the bare Common Name, not the "CN=..." distinguished form — the
        // install prompt drops it straight into a sentence.
        Assert.Equal("Masterwork Test Signer", result.CertificateCommonName);
    }

    [Fact]
    public void SignThenTamperContent_VerifyReturnsInvalid()
    {
        var cert = SelfSignedCertificateGenerator.Create("Masterwork Test Signer");
        var zip = MakeZip(("manifest.yaml", "id: 'x'"));
        var signed = PackageSigner.Sign(zip, cert);

        // Rebuild the zip with the same signature.sig but different manifest content — the
        // "tampered download" scenario the design's own trust-tier table calls a hard block.
        var sigBytes = ReadEntry(signed, "signature.sig");
        var tampered = MakeZip(("manifest.yaml", "id: 'tampered'"));
        var finalTampered = AppendEntry(tampered, "signature.sig", sigBytes);

        var result = PackageSigner.Verify(finalTampered);

        Assert.Equal(PackageVerificationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void SignThenTamperSignatureBytes_VerifyReturnsInvalid()
    {
        var cert = SelfSignedCertificateGenerator.Create("Masterwork Test Signer");
        var zip = MakeZip(("manifest.yaml", "id: 'x'"));
        var signed = PackageSigner.Sign(zip, cert);

        // JsonSerializer's default naming policy preserves the record's own PascalCase property
        // names ("Signature", not "signature") -- matching that here, not guessing camelCase.
        var sigJson = System.Text.Encoding.UTF8.GetString(ReadEntry(signed, "signature.sig"));
        Assert.Contains("\"Signature\":\"", sigJson);
        var corrupted = sigJson.Replace("\"Signature\":\"", "\"Signature\":\"AAAA");
        Assert.NotEqual(sigJson, corrupted);

        var finalCorrupted = AppendEntry(signed, "signature.sig", System.Text.Encoding.UTF8.GetBytes(corrupted));

        var result = PackageSigner.Verify(finalCorrupted);

        Assert.Equal(PackageVerificationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void Verify_MalformedSignatureFile_ReturnsInvalidNotThrow()
    {
        var zip = MakeZip(("manifest.yaml", "id: 'x'"), ("signature.sig", "not json"));

        var result = PackageSigner.Verify(zip);

        Assert.Equal(PackageVerificationOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public void SignTwice_SecondSignatureReplacesFirst_NotStacked()
    {
        var cert1 = SelfSignedCertificateGenerator.Create("Signer One");
        var cert2 = SelfSignedCertificateGenerator.Create("Signer Two");
        var zip = MakeZip(("manifest.yaml", "id: 'x'"));

        var signedOnce = PackageSigner.Sign(zip, cert1);
        var signedTwice = PackageSigner.Sign(signedOnce, cert2);

        using var stream = new MemoryStream(signedTwice);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Single(archive.Entries, e => e.FullName == "signature.sig");

        var result = PackageSigner.Verify(signedTwice);
        Assert.Equal(PackageVerificationOutcome.Valid, result.Outcome);
        Assert.Equal(cert2.Subject, result.CertificateSubject);
    }

    [Fact]
    public void EntryOrderDoesNotAffectVerification()
    {
        var cert = SelfSignedCertificateGenerator.Create("Masterwork Test Signer");
        var zipA = MakeZip(("a.txt", "one"), ("b.txt", "two"));
        var zipB = MakeZip(("b.txt", "two"), ("a.txt", "one"));

        var signedA = PackageSigner.Sign(zipA, cert);

        // Take signedA's signature.sig and graft it onto zipB (same logical content, different
        // physical entry order) -- verification must still succeed, since ComputeDigest sorts by
        // path rather than trusting the zip's own physical entry order.
        var sigBytes = ReadEntry(signedA, "signature.sig");
        var grafted = AppendEntry(zipB, "signature.sig", sigBytes);

        var result = PackageSigner.Verify(grafted);
        Assert.Equal(PackageVerificationOutcome.Valid, result.Outcome);
    }

    [Fact]
    public void SwappedEntryContent_SamePaths_VerificationFails()
    {
        // Same two paths, same signature.sig, but the *content* at each path swapped -- confirms
        // the digest binds path to content (not just a bag of content hashes), so this is caught.
        var cert = SelfSignedCertificateGenerator.Create("Masterwork Test Signer");
        var original = MakeZip(("a.txt", "one"), ("b.txt", "two"));
        var signed = PackageSigner.Sign(original, cert);
        var sigBytes = ReadEntry(signed, "signature.sig");

        var swapped = MakeZip(("a.txt", "two"), ("b.txt", "one"));
        var grafted = AppendEntry(swapped, "signature.sig", sigBytes);

        var result = PackageSigner.Verify(grafted);
        Assert.Equal(PackageVerificationOutcome.Invalid, result.Outcome);
    }
}
