using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>What an install flow should do about a package's signature, given who this build (and this player) already trusts.</summary>
public enum PackageTrustDecision
{
    /// <summary>Signed by the pinned anchor, or by a publisher the player already chose to trust — install with no prompt.</summary>
    Trusted,

    /// <summary>Validly signed, but by an identity neither pinned nor previously trusted — warn, showing the signer, and offer to remember it.</summary>
    UnrecognizedSigner,

    /// <summary>No signature at all — warn (no publisher identity, no tamper detection), allow after acknowledgment.</summary>
    Unsigned,

    /// <summary>A signature is present but doesn't verify — tampered, corrupted, or re-signed content. Never installable.</summary>
    Blocked,
}

/// <summary>
/// Turns <see cref="PackageSigner.Verify"/>'s factual result into the install flow's actual
/// behavior, given the build's pinned anchor and whatever publishers the player has already chosen
/// to trust. Pure and side-effect-free, so the same decision can be made in a test, at an install
/// call site, and (later) anywhere else content arrives from.
/// </summary>
public static class PackageTrustEvaluator
{
    /// <param name="pinnedThumbprint">This build's own pinned publisher — see <see cref="WhiteLabelConfig.PublisherThumbprint"/>.</param>
    /// <param name="trustedThumbprints">Publishers the player has previously chosen to trust going forward.</param>
    public static PackageTrustDecision Evaluate(
        SignatureVerificationResult verification,
        string? pinnedThumbprint,
        IEnumerable<string> trustedThumbprints)
    {
        switch (verification.Outcome)
        {
            case SignatureVerificationOutcome.Invalid:
                return PackageTrustDecision.Blocked;

            case SignatureVerificationOutcome.Unsigned:
                return PackageTrustDecision.Unsigned;

            case SignatureVerificationOutcome.Valid:
                // A Valid outcome always carries a thumbprint; the null guard is just so a
                // hand-constructed result can't silently match a null pinned anchor below.
                if (verification.CertificateThumbprint is not { Length: > 0 } thumbprint)
                {
                    return PackageTrustDecision.UnrecognizedSigner;
                }

                var pinned = pinnedThumbprint is { Length: > 0 }
                    && string.Equals(pinnedThumbprint, thumbprint, StringComparison.OrdinalIgnoreCase);
                var remembered = trustedThumbprints.Any(t => string.Equals(t, thumbprint, StringComparison.OrdinalIgnoreCase));

                return pinned || remembered ? PackageTrustDecision.Trusted : PackageTrustDecision.UnrecognizedSigner;

            default:
                return PackageTrustDecision.Blocked;
        }
    }
}
