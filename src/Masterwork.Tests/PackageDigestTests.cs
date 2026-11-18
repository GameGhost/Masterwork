using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

/// <summary>
/// The package digest is computed two ways — all at once on native heads, entry by entry through the
/// browser's crypto on the web head. They must agree exactly, or a package signed on one would fail
/// to verify on the other.
/// </summary>
public class PackageDigestTests
{
    private static byte[] Zip(params (string Path, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var stream = archive.CreateEntry(path).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    // What SignatureVerifier does on the browser, minus the interop: hash each entry on its own,
    // then combine.
    private static byte[] DigestEntryByEntry(byte[] zipBytes)
    {
        var hashes = PackageSigner.EnumerateSignedEntries(zipBytes)
            .Select(e => (e.Path, Hash: SHA256.HashData(PackageSigner.BuildEntryPreimage(e.Path, e.Bytes))));

        return PackageSigner.CombineEntryHashes(hashes);
    }

    [Fact]
    public void BothDigestPaths_Agree()
    {
        var zip = Zip(("manifest.yaml", "id: 'x'"), ("assets/a.txt", "one"), ("assets/b.txt", "two"));

        Assert.Equal(PackageSigner.ComputeContentDigest(zip), DigestEntryByEntry(zip));
    }

    [Fact]
    public void Digest_IsIndependentOfEntryOrder()
    {
        // A zip written by a different tool may store the same content in a different order.
        var forward = Zip(("a.txt", "one"), ("b.txt", "two"), ("c.txt", "three"));
        var reversed = Zip(("c.txt", "three"), ("b.txt", "two"), ("a.txt", "one"));

        Assert.Equal(PackageSigner.ComputeContentDigest(forward), PackageSigner.ComputeContentDigest(reversed));
    }

    [Fact]
    public void Digest_ChangesWhenContentChanges()
    {
        var original = Zip(("a.txt", "one"));
        var edited = Zip(("a.txt", "onf"));

        Assert.NotEqual(PackageSigner.ComputeContentDigest(original), PackageSigner.ComputeContentDigest(edited));
    }

    [Fact]
    public void Digest_ChangesWhenTwoEntriesSwapNames()
    {
        // The path is hashed with its content precisely so this can't pass unnoticed.
        var original = Zip(("a.txt", "one"), ("b.txt", "two"));
        var swapped = Zip(("a.txt", "two"), ("b.txt", "one"));

        Assert.NotEqual(PackageSigner.ComputeContentDigest(original), PackageSigner.ComputeContentDigest(swapped));
    }

    [Fact]
    public void Digest_IgnoresTheSignatureEntryItself()
    {
        var unsigned = Zip(("manifest.yaml", "id: 'x'"));
        var cert = SelfSignedCertificateGenerator.Create("Digest Test Signer");
        var signed = PackageSigner.Sign(unsigned, cert);

        // Adding the signature must not change what the signature covers, or signing would
        // invalidate itself.
        Assert.Equal(PackageSigner.ComputeContentDigest(unsigned), PackageSigner.ComputeContentDigest(signed));
    }

    [Fact]
    public void CountSignedEntries_MatchesWhatIsEnumerated()
    {
        var cert = SelfSignedCertificateGenerator.Create("Digest Test Signer");
        var signed = PackageSigner.Sign(Zip(("manifest.yaml", "id: 'x'"), ("assets/a.txt", "one")), cert);

        // Drives the browser path's progress denominator, so a mismatch would show a wrong total.
        Assert.Equal(
            PackageSigner.EnumerateSignedEntries(signed).Count(),
            PackageSigner.CountSignedEntries(signed));
    }

    [Fact]
    public void EnumerateSignedEntries_SkipsDirectoryEntries()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("assets/");
            using var stream = archive.CreateEntry("assets/a.txt").Open();
            stream.Write(Encoding.UTF8.GetBytes("one"));
        }

        Assert.Equal(["assets/a.txt"], PackageSigner.EnumerateSignedEntries(buffer.ToArray()).Select(e => e.Path));
    }
}
