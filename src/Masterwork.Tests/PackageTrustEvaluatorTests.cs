using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class PackageTrustEvaluatorTests
{
    private const string Pinned = "AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555FFFF6666AAAA7777BBBB8888";
    private const string Other = "9999888877776666555544443333222211110000FFFFEEEEDDDDCCCCBBBBAAAA";

    private static PackageVerificationResult Signed(string thumbprint) =>
        new(PackageVerificationOutcome.Valid, "CN=Someone", thumbprint);

    [Fact]
    public void InvalidSignature_IsBlocked_EvenIfThatPublisherIsTrusted()
    {
        var result = PackageTrustEvaluator.Evaluate(
            new PackageVerificationResult(PackageVerificationOutcome.Invalid),
            Pinned,
            [Pinned, Other]);

        Assert.Equal(PackageTrustDecision.Blocked, result);
    }

    [Fact]
    public void Unsigned_IsUnsigned_NotBlocked()
    {
        var result = PackageTrustEvaluator.Evaluate(
            new PackageVerificationResult(PackageVerificationOutcome.Unsigned),
            Pinned,
            []);

        Assert.Equal(PackageTrustDecision.Unsigned, result);
    }

    [Fact]
    public void SignedByPinnedAnchor_IsTrusted()
    {
        var result = PackageTrustEvaluator.Evaluate(Signed(Pinned), Pinned, []);

        Assert.Equal(PackageTrustDecision.Trusted, result);
    }

    [Fact]
    public void SignedByPinnedAnchor_ThumbprintCaseInsensitive()
    {
        var result = PackageTrustEvaluator.Evaluate(Signed(Pinned.ToLowerInvariant()), Pinned, []);

        Assert.Equal(PackageTrustDecision.Trusted, result);
    }

    [Fact]
    public void SignedByRememberedPublisher_IsTrusted()
    {
        var result = PackageTrustEvaluator.Evaluate(Signed(Other), Pinned, [Other]);

        Assert.Equal(PackageTrustDecision.Trusted, result);
    }

    [Fact]
    public void SignedByUnknownPublisher_IsUnrecognized()
    {
        var result = PackageTrustEvaluator.Evaluate(Signed(Other), Pinned, []);

        Assert.Equal(PackageTrustDecision.UnrecognizedSigner, result);
    }

    [Fact]
    public void NoPinnedAnchor_EverySignerIsUnrecognizedUntilTrusted()
    {
        // The current build's own state: nothing pinned, so a valid signature alone never means
        // "no prompt" — it only means the signer's identity can be shown and remembered.
        Assert.Equal(PackageTrustDecision.UnrecognizedSigner,
            PackageTrustEvaluator.Evaluate(Signed(Pinned), pinnedThumbprint: null, []));

        Assert.Equal(PackageTrustDecision.Trusted,
            PackageTrustEvaluator.Evaluate(Signed(Pinned), pinnedThumbprint: null, [Pinned]));
    }

    [Fact]
    public void EmptyPinnedAnchor_DoesNotMatchAMissingThumbprint()
    {
        // Guards the degenerate "" == "" match — an empty pinned anchor must not silently trust a
        // result that carries no thumbprint of its own.
        var result = PackageTrustEvaluator.Evaluate(
            new PackageVerificationResult(PackageVerificationOutcome.Valid, "CN=Someone", CertificateThumbprint: ""),
            pinnedThumbprint: "",
            []);

        Assert.Equal(PackageTrustDecision.UnrecognizedSigner, result);
    }
}
