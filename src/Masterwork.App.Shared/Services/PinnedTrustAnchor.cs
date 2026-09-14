namespace Masterwork.App.Shared.Services;

/// <summary>
/// The certificate thumbprint this build treats as its own publisher — content signed by it
/// installs with no prompt at all (see <see cref="PackageTrustEvaluator"/>).
/// <see langword="null"/> means nothing is pre-trusted, so every signed package goes through the
/// unrecognized-signer prompt until a real anchor is pinned here.
/// </summary>
/// <remarks>
/// A stand-in: eventually each white-label build carries its own pinned certificate as part of its
/// own build-time configuration, rather than one constant shared by every flavor. Kept as a single
/// place to change so moving it later touches nothing but this file and whatever supplies the value.
/// </remarks>
public static class PinnedTrustAnchor
{
    /// <summary>
    /// SHA-256 thumbprint (hex, case-insensitive), or <see langword="null"/> when this build pins
    /// nothing. Currently the Masterwork content-signing certificate — the identity
    /// <c>Masterwork-Modules</c> signs its releases with. Rotating that certificate means changing
    /// this value and shipping an app update; there is no revocation mechanism beyond that.
    /// </summary>
    public static string? Thumbprint => "15B0433E3B600F11CB5A8F3B6222C9F907CF140F7024839A3703A783C3327C59";
}
