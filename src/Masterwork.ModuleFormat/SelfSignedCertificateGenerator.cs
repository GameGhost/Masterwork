using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Masterwork.ModuleFormat;

/// <summary>
/// Generates the one self-signed certificate a project maintainer holds to sign canonical content —
/// no CA, no chain, rotated by generating a new one and re-pinning its thumbprint in a future app
/// build. RSA 3072-bit, matching <see cref="PackageSigner"/>'s own RSA/SHA-256/PKCS1 signing scheme.
/// </summary>
public static class SelfSignedCertificateGenerator
{
    /// <summary>
    /// Creates a new self-signed certificate with a fresh RSA key pair. <paramref name="subjectName"/>
    /// becomes the certificate's distinguished name — pass a plain string (e.g. <c>"Masterwork Content Signing"</c>),
    /// not a pre-formed <c>CN=...</c> string; this wraps it as the Common Name itself.
    /// </summary>
    public static X509Certificate2 Create(string subjectName, int validityYears = 1)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5); // small back-dating tolerates minor clock skew on first use
        var notAfter = notBefore.AddYears(validityYears);

        using var ephemeral = request.CreateSelfSigned(notBefore, notAfter);

        // CreateSelfSigned's own private key can be ephemeral, tied to `rsa`'s own lifetime on some
        // platforms — round-tripping through a PFX export/import detaches the returned certificate
        // from it, so the result stays safely usable (including exporting it again later, e.g.
        // Masterwork.ModulePacker's `gencert` mode) after this method returns and both `rsa` and
        // `ephemeral` are disposed.
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), password: null, X509KeyStorageFlags.Exportable);
    }
}
