using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class DetachedSignatureTests
{
    private static byte[] Content(string text = """{ "format": "mwcatalog/1", "entries": [] }""") =>
        Encoding.UTF8.GetBytes(text);

    [Fact]
    public void SignThenVerify_ReportsValidAndTheSignerIdentity()
    {
        var cert = SelfSignedCertificateGenerator.Create("Catalog Test Signer");
        var content = Content();

        var result = DetachedSignature.Verify(content, DetachedSignature.Sign(content, cert));

        Assert.Equal(SignatureVerificationOutcome.Valid, result.Outcome);
        Assert.Equal("Catalog Test Signer", result.CertificateCommonName);
        Assert.Equal(cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256), result.CertificateThumbprint);
    }

    [Fact]
    public void Verify_NoSignature_IsUnsigned()
    {
        Assert.Equal(SignatureVerificationOutcome.Unsigned, DetachedSignature.Verify(Content(), null).Outcome);
        Assert.Equal(SignatureVerificationOutcome.Unsigned, DetachedSignature.Verify(Content(), []).Outcome);
    }

    [Fact]
    public void Verify_ContentChangedAfterSigning_IsInvalid()
    {
        var cert = SelfSignedCertificateGenerator.Create("Catalog Test Signer");
        var signature = DetachedSignature.Sign(Content(), cert);

        var result = DetachedSignature.Verify(Content("""{ "format": "mwcatalog/1", "entries": [1] }"""), signature);

        Assert.Equal(SignatureVerificationOutcome.Invalid, result.Outcome);
        Assert.Null(result.CertificateCommonName);
    }

    [Fact]
    public void Verify_WhitespaceOnlyChange_IsInvalid()
    {
        // The digest covers literal bytes, not parsed JSON — re-serializing the same data differently
        // is a real mismatch, which is exactly why the catalog is signed as bytes.
        var cert = SelfSignedCertificateGenerator.Create("Catalog Test Signer");
        var signature = DetachedSignature.Sign(Content(), cert);

        var reformatted = Content("""{"format":"mwcatalog/1","entries":[]}""");

        Assert.Equal(SignatureVerificationOutcome.Invalid, DetachedSignature.Verify(reformatted, signature).Outcome);
    }

    [Fact]
    public void Verify_CorruptedSignature_IsInvalid()
    {
        var cert = SelfSignedCertificateGenerator.Create("Catalog Test Signer");
        var content = Content();
        var signature = DetachedSignature.Sign(content, cert);

        Assert.Equal(SignatureVerificationOutcome.Invalid, DetachedSignature.Verify(content, Encoding.UTF8.GetBytes("not json")).Outcome);

        // A structurally-valid envelope whose signature bytes were tampered with.
        var json = Encoding.UTF8.GetString(signature);
        var tampered = json.Replace("\"Signature\":\"", "\"Signature\":\"AA");
        Assert.NotEqual(json, tampered);
        Assert.Equal(SignatureVerificationOutcome.Invalid, DetachedSignature.Verify(content, Encoding.UTF8.GetBytes(tampered)).Outcome);
    }

    [Fact]
    public void Verify_SignedByADifferentKey_IsInvalid()
    {
        var content = Content();
        var signature = DetachedSignature.Sign(content, SelfSignedCertificateGenerator.Create("Signer One"));
        var otherSignature = DetachedSignature.Sign(Content("other"), SelfSignedCertificateGenerator.Create("Signer Two"));

        Assert.Equal(SignatureVerificationOutcome.Invalid, DetachedSignature.Verify(content, otherSignature).Outcome);
        Assert.Equal(SignatureVerificationOutcome.Valid, DetachedSignature.Verify(content, signature).Outcome);
    }
}
