namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAssetResolver"/>
public sealed class AssetResolver(GameSessionState sessionState) : IAssetResolver
{
    private const string IconScheme = "icon://";
    private const string ImageScheme = "image://";

    // Stand-in for the MFW_Common_Assets dependency pack, which doesn't exist until asset packs are
    // unshelved (masterwork-plan Q27). These are small hand-authored placeholder SVGs
    // (wwwroot/assets/test-pack/), not derived from any copyrighted source — real assets are a
    // drop-in replacement, same slugs. icon:// only; image:// has no placeholder pack.
    private static readonly IReadOnlyDictionary<string, string> TestAssetPack = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["village"] = "_content/Masterwork.App.Shared/assets/test-pack/village.svg",
        ["hospital"] = "_content/Masterwork.App.Shared/assets/test-pack/hospital.svg",
        ["creepy"] = "_content/Masterwork.App.Shared/assets/test-pack/creepy.svg",
    };

    private const string FallbackIcon = "_content/Masterwork.App.Shared/assets/fallback-icon.svg";

    // Checked in order against "assets/{folder}/{slug}{ext}" — modules ship images as plain files
    // (renamed to match their slug, see cost-of-disease's asset-inventory doc), not a manifest of
    // extensions, so the resolver just tries the common ones.
    private static readonly (string Ext, string MimeType)[] ImageExtensions =
    [
        (".png", "image/png"),
        (".svg", "image/svg+xml"),
        (".jpg", "image/jpeg"),
        (".jpeg", "image/jpeg"),
    ];

    /// <inheritdoc/>
    public Task<string?> ResolveAsync(string assetUri)
    {
        string scheme;
        string folder;
        if (assetUri.StartsWith(IconScheme, StringComparison.Ordinal))
        {
            scheme = IconScheme;
            folder = "icons";
        }
        else if (assetUri.StartsWith(ImageScheme, StringComparison.Ordinal))
        {
            scheme = ImageScheme;
            folder = "images";
        }
        else
        {
            return Task.FromResult<string?>(null);
        }

        var slug = assetUri[scheme.Length..];

        // Tier 1 (bundle-local): the currently-loaded module's own assets, from GameSessionState
        // (populated by IModuleStore.LoadAsync — see LoadedModuleContent). Bytes are returned as a
        // data: URI rather than a blob URL or temp file, so resolution is identical on WASM and
        // MAUI BlazorWebView — the only platform difference is where LoadAsync's bytes came from.
        if (TryResolveBundleLocal(folder, slug) is { } dataUri)
        {
            return Task.FromResult<string?>(dataUri);
        }

        // Tier 2 (dependency pack) — icon:// only; image:// has no placeholder pack yet.
        if (scheme == IconScheme && TestAssetPack.TryGetValue(slug, out var packUrl))
        {
            return Task.FromResult<string?>(packUrl);
        }

        // Tier 3 (engine fallback) — icon:// only; an unresolved image:// yields null (the caller,
        // e.g. RenderedImageView, shows its own "missing image" state).
        return Task.FromResult<string?>(scheme == IconScheme ? FallbackIcon : null);
    }

    private string? TryResolveBundleLocal(string folder, string slug)
    {
        foreach (var (ext, mime) in ImageExtensions)
        {
            if (sessionState.Assets.TryGetValue($"assets/{folder}/{slug}{ext}", out var bytes))
            {
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
        }

        return null;
    }
}
