using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Signs and verifies a file that can't carry a signature inside itself — the catalog, published as
/// <c>catalog.json</c> alongside a <c>catalog.json.sig</c> holding what <see cref="Sign"/> returns.
/// The digest covers the file's literal bytes as downloaded, so the catalog stays readable and
/// diffable and there is no canonical-JSON problem to get wrong: re-serializing the same data with a
/// different writer would produce different bytes and correctly fail verification.
/// </summary>
public static class DetachedSignature
{
    /// <summary>Returns the signature envelope for <paramref name="content"/>, to be published beside it.</summary>
    /// <param name="signingCertificate">Must have an RSA private key attached (e.g. loaded from a <c>.pfx</c>).</param>
    public static byte[] Sign(byte[] content, X509Certificate2 signingCertificate) =>
        SignatureEnvelope.Build(SHA256.HashData(content), signingCertificate);

    /// <summary>
    /// Verifies <paramref name="content"/> against its detached signature. A <see langword="null"/>
    /// or empty <paramref name="signatureJson"/> reports <see cref="SignatureVerificationOutcome.Unsigned"/>
    /// — the caller decides what to do about a source that serves no signature, the same way it
    /// decides about an unsigned package.
    /// </summary>
    public static SignatureVerificationResult Verify(byte[] content, byte[]? signatureJson)
    {
        if (signatureJson is not { Length: > 0 })
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Unsigned);
        }

        return SignatureEnvelope.VerifyDigest(SHA256.HashData(content), signatureJson);
    }
}
