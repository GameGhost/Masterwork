namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAssetResolver"/>
public sealed class AssetResolver(GameSessionState sessionState) : IAssetResolver
{
    private const string IconScheme = "icon://";
    private const string ImageScheme = "image://";
    private const string FontScheme = "font://";

    // Keyed by the raw assetUri. Blazor re-renders a component tree (and thus re-resolves every
    // {icon:...}/image node it contains) far more often than the underlying module assets ever
    // change, and a bundle-local hit is otherwise a full Convert.ToBase64String over the raw asset
    // bytes on every single render — the dominant cost behind popups/text feeling sluggish,
    // especially on single-threaded WASM. This instance can outlive a single module (it's DI-scoped,
    // which on WASM/MAUI BlazorWebView effectively means "app lifetime" — Abandon/Save-and-quit
    // return to the main menu without tearing down the service provider), so the cache is only valid
    // as long as GameSessionState.Assets is still the same dictionary instance this resolver last
    // saw; a new module load replaces that reference wholesale (see GameSessionState), which is the
    // signal to invalidate rather than serve another module's stale resolved bytes.
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, byte[]>? _cachedForAssets;

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

    // Same "try the common extensions against the bare slug" convention as ImageExtensions —
    // module fonts ship as plain files under assets/fonts/, named to match the slug.
    private static readonly (string Ext, string MimeType)[] FontExtensions =
    [
        (".woff2", "font/woff2"),
        (".woff", "font/woff"),
        (".ttf", "font/ttf"),
        (".otf", "font/otf"),
    ];

    /// <inheritdoc/>
    public Task<string?> ResolveAsync(string assetUri)
    {
        if (!ReferenceEquals(_cachedForAssets, sessionState.Assets))
        {
            _cache.Clear();
            _cachedForAssets = sessionState.Assets;
        }

        if (_cache.TryGetValue(assetUri, out var cached))
        {
            return Task.FromResult(cached);
        }

        var resolved = ResolveUncached(assetUri);
        _cache[assetUri] = resolved;
        return Task.FromResult(resolved);
    }

    private string? ResolveUncached(string assetUri)
    {
        string scheme;
        string folder;
        (string Ext, string MimeType)[] extensions;
        if (assetUri.StartsWith(IconScheme, StringComparison.Ordinal))
        {
            scheme = IconScheme;
            folder = "icons";
            extensions = ImageExtensions;
        }
        else if (assetUri.StartsWith(ImageScheme, StringComparison.Ordinal))
        {
            scheme = ImageScheme;
            folder = "images";
            extensions = ImageExtensions;
        }
        else if (assetUri.StartsWith(FontScheme, StringComparison.Ordinal))
        {
            scheme = FontScheme;
            folder = "fonts";
            extensions = FontExtensions;
        }
        else
        {
            return null;
        }

        var slug = assetUri[scheme.Length..];

        // Tier 1 (bundle-local): the currently-loaded module's own assets, from GameSessionState
        // (populated by IModuleStore.LoadAsync — see LoadedModuleContent). Bytes are returned as a
        // data: URI rather than a blob URL or temp file, so resolution is identical on WASM and
        // MAUI BlazorWebView — the only platform difference is where LoadAsync's bytes came from.
        if (TryResolveBundleLocal(folder, slug, extensions) is { } dataUri)
        {
            return dataUri;
        }

        // Tier 2 (dependency pack) — icon:// only; image:// and font:// have no placeholder pack yet.
        if (scheme == IconScheme && TestAssetPack.TryGetValue(slug, out var packUrl))
        {
            return packUrl;
        }

        // Tier 3 (engine fallback) — icon:// only; an unresolved image:// or font:// yields null
        // (the caller, e.g. RenderedImageView, shows its own "missing image" state; an unresolved
        // font:// in a @font-face rule just leaves that face unavailable, same as any 404'd font).
        return scheme == IconScheme ? FallbackIcon : null;
    }

    private string? TryResolveBundleLocal(string folder, string slug, (string Ext, string MimeType)[] extensions)
    {
        foreach (var (ext, mime) in extensions)
        {
            if (sessionState.Assets.TryGetValue($"assets/{folder}/{slug}{ext}", out var bytes))
            {
                return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            }
        }

        return null;
    }
}
