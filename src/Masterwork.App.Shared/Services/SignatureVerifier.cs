using System.Security.Cryptography;
using Masterwork.ModuleFormat;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Verifies content signatures on whichever platform the app is running on.
///
/// Native heads use the managed implementation directly. The browser runtime has no
/// <c>System.Security.Cryptography.X509Certificates</c> at all — reading a certificate there throws
/// <see cref="PlatformNotSupportedException"/> — so the web head verifies through
/// <c>wwwroot/signatureVerify.js</c> and the browser's own Web Crypto instead. Only the 32-byte
/// digest, the certificate, and the signature ever cross the interop boundary, whatever the size of
/// the content behind them.
/// </summary>
public sealed class SignatureVerifier(IJSRuntime js, BrowserCrypto hasher, ILogger<SignatureVerifier> logger)
{
    private IJSObjectReference? _module;

    private sealed record JsResult(bool Valid, string? Thumbprint, string? CommonName);

    /// <summary>Verifies a detached signature over <paramref name="content"/>'s exact bytes.</summary>
    public Task<SignatureVerificationResult> VerifyDetachedAsync(byte[] content, byte[]? signatureJson)
    {
        if (signatureJson is not { Length: > 0 })
        {
            return Task.FromResult(new SignatureVerificationResult(SignatureVerificationOutcome.Unsigned));
        }

        return VerifyAsync(SHA256.HashData(content), signatureJson);
    }

    /// <summary>
    /// Verifies a package's embedded <c>signature.sig</c>, if it has one. <paramref name="progress"/>
    /// reports (entries hashed, total) — worth showing, since hashing a large package's entries is
    /// the slow part.
    /// </summary>
    public async Task<SignatureVerificationResult> VerifyPackageAsync(
        byte[] packageBytes,
        IProgress<(int Done, int Total)>? progress = null)
    {
        // Finding the signature needs no certificate API, so it works everywhere; only hashing and
        // the signature check itself differ by platform.
        if (PackageSigner.ReadSignatureEntry(packageBytes) is not { } signatureJson)
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Unsigned);
        }

        var digest = OperatingSystem.IsBrowser()
            ? await ComputeContentDigestViaBrowserAsync(packageBytes, progress)
            : PackageSigner.ComputeContentDigest(packageBytes);

        return await VerifyAsync(digest, signatureJson);
    }

    // Hashes each entry through the browser's own crypto, one at a time. Managed SHA-256 runs at
    // roughly 1.2 s/MB under the WASM interpreter, so hashing a 56MB package that way freezes the UI
    // for about a minute; this keeps peak memory to a single entry and the cost to native speed,
    // and the await between entries lets the UI paint.
    private async Task<byte[]> ComputeContentDigestViaBrowserAsync(
        byte[] packageBytes,
        IProgress<(int Done, int Total)>? progress)
    {
        var entryHashes = new List<(string Path, byte[] Hash)>();

        // Counted first so progress has a denominator; this reads the zip directory only.
        var total = PackageSigner.CountSignedEntries(packageBytes);
        progress?.Report((0, total));

        foreach (var (path, bytes) in PackageSigner.EnumerateSignedEntries(packageBytes))
        {
            var hex = await hasher.Sha256HexAsync(PackageSigner.BuildEntryPreimage(path, bytes));
            entryHashes.Add((path, Convert.FromHexString(hex)));
            progress?.Report((entryHashes.Count, total));
        }

        return PackageSigner.CombineEntryHashes(entryHashes);
    }

    private async Task<SignatureVerificationResult> VerifyAsync(byte[] digest, byte[] signatureJson)
    {
        if (!OperatingSystem.IsBrowser())
        {
            return SignatureEnvelope.VerifyDigest(digest, signatureJson);
        }

        if (SignatureEnvelope.TryRead(signatureJson) is not { } envelope)
        {
            return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
        }

        try
        {
            _module ??= await js.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Masterwork.App.Shared/signatureVerify.js");

            var result = await _module.InvokeAsync<JsResult>(
                "verifyDigest", digest, envelope.Certificate, envelope.Signature);

            if (!result.Valid)
            {
                return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
            }

            // No distinguished-name string out here: the browser path reads the Common Name only,
            // which is what the install prompts actually show.
            return new SignatureVerificationResult(
                SignatureVerificationOutcome.Valid,
                CertificateSubject: result.CommonName is null ? null : $"CN={result.CommonName}",
                CertificateThumbprint: result.Thumbprint,
                CertificateCommonName: result.CommonName);
        }
        catch (JSException ex)
        {
            // crypto.subtle needs a secure context (HTTPS or localhost). Treating this as "can't
            // verify" rather than "invalid" would be worse: it would let unverified content install.
            logger.LogError(ex, "Browser signature verification failed");
            return new SignatureVerificationResult(SignatureVerificationOutcome.Invalid);
        }
    }
}
