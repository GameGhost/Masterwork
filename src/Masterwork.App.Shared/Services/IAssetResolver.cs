namespace Masterwork.App.Shared.Services;

/// <summary>
/// Resolves an asset reference (currently just <c>icon://slug</c>) to a URL the UI can render.
/// Mirrors the three-tier model from the design doc's App Skinning section: bundle-local (the
/// module's own assets — not usable yet, since <c>LoadFromSources</c> doesn't carry a directory;
/// arrives with real module loading in Phase 3), dependency pack (<c>MFW_Common_Assets</c> — also
/// Phase 3; stood in for here by a small hand-authored <see cref="AssetResolver"/>-internal test
/// pack), and an engine-provided fallback that's always available.
/// </summary>
public interface IAssetResolver
{
    /// <summary>Resolves <paramref name="assetUri"/> to a URL, or <see langword="null"/> if the reference isn't a supported form (e.g. not <c>icon://</c>).</summary>
    Task<string?> ResolveAsync(string assetUri);
}
