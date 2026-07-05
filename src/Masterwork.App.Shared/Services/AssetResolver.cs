namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAssetResolver"/>
public sealed class AssetResolver : IAssetResolver
{
    private const string IconScheme = "icon://";

    // Stand-in for the MFW_Common_Assets dependency pack, which doesn't exist until Phase 3's MWM
    // packaging. These are small hand-authored placeholder SVGs (wwwroot/assets/test-pack/), not
    // derived from any copyrighted source — real assets are a drop-in replacement, same slugs.
    private static readonly IReadOnlyDictionary<string, string> TestAssetPack = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["village"] = "_content/Masterwork.App.Shared/assets/test-pack/village.svg",
        ["hospital"] = "_content/Masterwork.App.Shared/assets/test-pack/hospital.svg",
        ["creepy"] = "_content/Masterwork.App.Shared/assets/test-pack/creepy.svg",
    };

    private const string FallbackIcon = "_content/Masterwork.App.Shared/assets/fallback-icon.svg";

    /// <inheritdoc/>
    public Task<string?> ResolveAsync(string assetUri)
    {
        if (!assetUri.StartsWith(IconScheme, StringComparison.Ordinal))
        {
            return Task.FromResult<string?>(null);
        }

        var slug = assetUri[IconScheme.Length..];

        // Tier 1 (bundle-local) intentionally has no code path yet — see the type doc comment.
        var url = TestAssetPack.TryGetValue(slug, out var packUrl) ? packUrl : FallbackIcon;
        return Task.FromResult<string?>(url);
    }
}
