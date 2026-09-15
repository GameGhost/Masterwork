namespace Masterwork.App.Shared.Services;

/// <summary>
/// What distinguishes one white-label build from another: where its content comes from, and whose
/// signature it trusts without asking.
/// </summary>
/// <remarks>
/// These belong together because they are one decision. The pinned thumbprint is meaningful only
/// against the source it was pinned for — a flavor shipping against a different catalog is
/// necessarily trusting a different publisher, and pinning one while pointing at the other would
/// silently downgrade every install to an unrecognized-signer prompt.
///
/// A stand-in for the real build-time configuration 6.5 introduces. Kept as a single place to change
/// so moving it later touches nothing but this file and whatever supplies the values.
/// </remarks>
public static class WhiteLabelConfig
{
    /// <summary>
    /// Upstream catalog URL. The catalog on the default branch is always the current one: a release
    /// is published first (tag plus assets), then the regenerated <c>catalog.json</c> and its
    /// signature are committed, so the catalog never names an asset that isn't there yet.
    /// </summary>
    public const string CatalogUrl = "https://raw.githubusercontent.com/GameGhost/Masterwork-Modules/main/catalog.json";

    /// <summary>
    /// Upstream base that every catalog entry's relative path resolves against. Entry paths start
    /// with the release tag (<c>v0.4.0/cost-of-disease.mwm</c>), so one catalog can point at
    /// packages from several different releases at once — a module that hasn't changed keeps
    /// pointing at the older tag it was published under.
    ///
    /// A different host from <see cref="CatalogUrl"/> because GitHub serves repository files and
    /// release assets separately; that split is safe precisely because both values are compiled in
    /// rather than read out of the catalog.
    /// </summary>
    public const string PackageBaseUrl = "https://github.com/GameGhost/Masterwork-Modules/releases/download";

    /// <summary>
    /// SHA-256 thumbprint (hex, case-insensitive) of the certificate this build treats as its own
    /// publisher: content signed by it installs with no prompt, and a catalog signed by it is
    /// trusted enough for its entry hashes to stand in for per-package signatures.
    /// <see langword="null"/> means nothing is pre-trusted, so every signed package goes through the
    /// unrecognized-signer prompt.
    ///
    /// Currently the Masterwork content-signing certificate — the identity <c>Masterwork-Modules</c>
    /// signs its releases with. Rotating that certificate means changing this value and shipping an
    /// app update; there is no revocation mechanism beyond that.
    /// </summary>
    public const string? PublisherThumbprint = "15B0433E3B600F11CB5A8F3B6222C9F907CF140F7024839A3703A783C3327C59";

    /// <summary>
    /// The built-in source, for heads that fetch upstream directly. The web head does not use this —
    /// its client is hard-wired to its own origin, and the *site* is configured with the upstream
    /// values instead, so the browser never needs to reach upstream at all.
    /// </summary>
    public static ContentSource PrimarySource { get; } = new(CatalogUrl, PackageBaseUrl);
}
