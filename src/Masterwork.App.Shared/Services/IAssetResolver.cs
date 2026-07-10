namespace Masterwork.App.Shared.Services;

/// <summary>
/// Resolves an asset reference (<c>icon://slug</c> or <c>image://slug</c>) to a URL the UI can
/// render. Implements the three-tier model from the design doc's App Skinning section:
/// bundle-local (the currently-loaded module's own assets, via <see cref="GameSessionState.Assets"/>),
/// dependency pack (<c>MFW_Common_Assets</c> — shelved per Q27; stood in for here by a small
/// hand-authored <see cref="AssetResolver"/>-internal test pack), and an engine-provided fallback
/// that's always available (<c>icon://</c> only).
/// </summary>
public interface IAssetResolver
{
    /// <summary>Resolves <paramref name="assetUri"/> to a URL, or <see langword="null"/> if the reference isn't a supported form or scheme, or (for <c>image://</c> specifically) isn't found in any tier.</summary>
    Task<string?> ResolveAsync(string assetUri);
}
