using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Masterwork.ModuleFormat;

/// <summary>
/// What a signature check found. A fact, not a trust decision — deciding whether the signer is
/// *trusted* is caller-side policy (comparing the thumbprint against a pinned anchor or a "trusted
/// publishers" list).
/// </summary>
public enum SignatureVerificationOutcome
{
    /// <summary>Nothing to check — no embedded <c>signature.sig</c> entry, or no detached signature supplied.</summary>
    Unsigned,

    /// <summary>A signature is present and verifies against its own embedded certificate.</summary>
    Valid,

    /// <summary>A signature is present but malformed, or doesn't verify against the actual content (tampered, corrupted, or wrong key).</summary>
    Invalid,
}

/// <summary>
/// <see cref="SignatureVerificationOutcome.Valid"/> carries the signer's identity so a caller can
/// render the trust-tier UI (compare <see cref="CertificateThumbprint"/> against a pinned anchor or
/// a remembered "trust this publisher" choice, name the publisher in an unrecognized-signer warning)
/// without re-parsing the signed content itself. <see cref="CertificateCommonName"/> is the one to
/// show a player — the bare Common Name, e.g. <c>Masterwork Content Signing</c>;
/// <see cref="CertificateSubject"/> is the full distinguished name behind it. All are
/// <see langword="null"/> for <see cref="SignatureVerificationOutcome.Unsigned"/> and
/// <see cref="SignatureVerificationOutcome.Invalid"/> (an invalid signature's claimed certificate
/// isn't trustworthy information — reading a corrupted/hostile signature far enough to identify
/// *whose* signature failed isn't attempted).
/// </summary>
public sealed record SignatureVerificationResult(
    SignatureVerificationOutcome Outcome,
    string? CertificateSubject = null,
    string? CertificateThumbprint = null,
    string? CertificateCommonName = null
);

/// <summary>
/// The one on-the-wire signature format this project uses, shared by a package's embedded
/// <c>signature.sig</c> entry (<see cref="PackageSigner"/>) and the catalog's detached
/// <c>catalog.json.sig</c> (<see cref="DetachedSignature"/>). Both sign a SHA-256 digest with
/// RSA/PKCS#1 and carry the signer's certificate along with the signature, so verification needs
/// nothing but the signed content and this envelope. What differs between them is only *what* the
/// digest covers — every zip entry in one case, the literal file bytes in the other.
/// </summary>
internal static class SignatureEnvelope
{
    private const string Algorithm = "RS256";

    private sealed record SignatureFile(string Algorithm, string Certificate, string Signature);

    /// <summary>Signs <paramref name="digest"/> and serializes the result to the envelope's JSON form.</summary>
    /// <param name="signingCertificate">Must have an RSA private key attached (e.g. loaded from a <c>.pfx</c>).</param>
    internal static byte[] Build(byte[] digest, X509Certificate2 signingCertificate)
    {
        using var rsa = signingCertificate.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("Signing certificate has no RSA private key — load it from a .pfx that includes one.");

        var signatureBytes = rsa.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        return JsonSerializer.SerializeToUtf8Bytes(new SignatureFile(
            Algorithm,
            Certificate: Convert.ToBase64String(signingCertificate.Export(X509ContentType.Cert)),
            Signature: Convert.ToBase64String(signatureBytes)));
    }

    /// <summary>
    /// Checks an envelope against the digest it should cover. Every malformed-input path collapses to
    /// <see cref="SignatureVerificationOutcome.Invalid"/> deliberately: a caller can't do anything
    /// useful with "the base64 was bad" that it wouldn't do with "the signature didn't match", and
    /// distinguishing them would mean reporting detail read out of untrusted bytes.
    /// </summary>
    internal static SignatureVerificationResult VerifyDigest(byte[] digest, byte[] signatureJson)
    {
        SignatureFile? signatureFile;
        try
        {
            signatureFile = JsonSerializer.Deserialize<SignatureFile>(signatureJson);
        }
        catch (JsonException)
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
        }

        if (signatureFile is not { Algorithm: Algorithm })
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
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
            return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
        }

        using var rsa = cert.GetRSAPublicKey();
        if (rsa is null)
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
        }

        if (!rsa.VerifyHash(digest, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
        }

        return new SignatureVerificationResult(
            SignatureVerificationOutcome.Valid,
            cert.Subject,
            cert.GetCertHashString(HashAlgorithmName.SHA256),
            cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false));
    }
}
