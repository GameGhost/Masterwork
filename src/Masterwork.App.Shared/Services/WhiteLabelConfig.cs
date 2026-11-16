namespace Masterwork.App.Shared.Services;

/// <summary>
/// What distinguishes one white-label build from another at runtime: what the app calls itself,
/// where its content comes from, and whose signature it trusts without asking. Supplied by the
/// flavor's <see cref="IThemeModule"/> and registered as a singleton.
/// </summary>
/// <remarks>
/// The source and the pinned thumbprint belong together because they are one decision. The pinned
/// thumbprint is meaningful only against the source it was pinned for — a flavor shipping against a
/// different catalog is necessarily trusting a different publisher, and pinning one while pointing
/// at the other would silently downgrade every install to an unrecognized-signer prompt.
///
/// OS-level identity (application id, store title, icons) is not here: the platform reads those at
/// build time, so they live in the flavor's MSBuild props instead.
/// </remarks>
public sealed record WhiteLabelConfig
{
    /// <summary>The app's in-app name — page titles, window title, the web app manifest.</summary>
    public required string AppName { get; init; }

    /// <summary>A short form of <see cref="AppName"/>, for places with little room (the PWA <c>short_name</c>).</summary>
    public required string ShortName { get; init; }

    /// <summary>
    /// The game this flavor accompanies, as it appears in running text (the main menu tagline, help)
    /// and as the accessible name of the theme's logo.
    /// </summary>
    public required string GameName { get; init; }

    /// <summary>
    /// Who publishes <see cref="GameName"/>, for the app's statement that it is unofficial and not
    /// affiliated with them.
    /// </summary>
    public required string GamePublisher { get; init; }

    /// <summary>Browser/PWA chrome colour (<c>theme-color</c>, manifest background), as a CSS colour.</summary>
    public required string ThemeColor { get; init; }

    /// <summary>
    /// Upstream catalog URL — normally the flavor site's <see cref="ContentApi"/> route
    /// (<see cref="ContentApi.ForSite"/>), so where the content is stored can change without an app
    /// update.
    /// </summary>
    public required string CatalogUrl { get; init; }

    /// <summary>
    /// Upstream base that every catalog entry's relative path resolves against. May be a different
    /// host from <see cref="CatalogUrl"/>; that split is safe precisely because both values are
    /// compiled in rather than read out of the catalog.
    /// </summary>
    public required string PackageBaseUrl { get; init; }

    /// <summary>
    /// SHA-256 thumbprint (hex, case-insensitive) of the certificate this build treats as its own
    /// publisher: content signed by it installs with no prompt, and a catalog signed by it is
    /// trusted enough for its entry hashes to stand in for per-package signatures.
    /// <see langword="null"/> means nothing is pre-trusted, so every signed package goes through the
    /// unrecognized-signer prompt. Rotating it means shipping an app update; there is no revocation
    /// mechanism beyond that.
    /// </summary>
    public string? PublisherThumbprint { get; init; }

    /// <summary>
    /// The built-in source, for heads that fetch upstream directly. The web head does not use this —
    /// its client is hard-wired to its own origin, and the *site* is configured with the upstream
    /// values instead, so the browser never needs to reach upstream at all.
    /// </summary>
    public ContentSource PrimarySource => new(CatalogUrl, PackageBaseUrl);
}
