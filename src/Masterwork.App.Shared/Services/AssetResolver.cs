namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAssetResolver"/>
public sealed class AssetResolver(GameSessionState sessionState) : IAssetResolver
{
    private const string IconScheme = "icon://";
    private const string ImageScheme = "image://";
    private const string FontScheme = "font://";
    private const string AudioScheme = "audio://";

    // Keyed by the raw assetUri. Blazor re-renders a component tree (and thus re-resolves every
    // {icon:...}/image node it contains) far more often than the underlying module assets ever
    // change, and a bundle-local hit is otherwise a fresh IModuleAssetSource.GetAssetUrlAsync call on
    // every single render — worth avoiding even though PreloadedModuleAssetSource itself also caches,
    // since this cache is keyed by the full assetUri (post scheme/slug resolution), one dictionary
    // lookup away rather than a re-derived path each time. This instance can outlive a single module (it's DI-scoped,
    // which on WASM/MAUI BlazorWebView effectively means "app lifetime" — Abandon/Save-and-quit
    // return to the main menu without tearing down the service provider), so the cache is only valid
    // as long as GameSessionState.Assets is still the same dictionary instance this resolver last
    // saw; a new module load replaces that reference wholesale (see GameSessionState), which is the
    // signal to invalidate rather than serve another module's stale resolved bytes.
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private IModuleAssetSource? _cachedForAssets;

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

    // Same convention again for audio:// — one scheme, not three (bgm/sfx/vo are folder-naming
    // conventions within it, not separate schemes; see docs/mws-format-latest.md §6).
    private static readonly (string Ext, string MimeType)[] AudioExtensions =
    [
        (".mp3", "audio/mpeg"),
        (".ogg", "audio/ogg"),
        (".wav", "audio/wav"),
    ];

    /// <inheritdoc/>
    public async Task<string?> ResolveAsync(string assetUri)
    {
        if (!ReferenceEquals(_cachedForAssets, sessionState.Assets))
        {
            _cache.Clear();
            _cachedForAssets = sessionState.Assets;
        }

        if (_cache.TryGetValue(assetUri, out var cached))
        {
            return cached;
        }

        var resolved = await ResolveUncachedAsync(assetUri);
        _cache[assetUri] = resolved;
        return resolved;
    }

    private async Task<string?> ResolveUncachedAsync(string assetUri)
    {
        // audio:// gets its own dedicated path rather than joining the scheme dispatch below —
        // unlike icon/image/font it needs a culture-suffix probe first (see TryResolveAudioAsync),
        // and it deliberately has only one resolution tier (no dependency-pack/engine-fallback —
        // see that method's own remarks), so it doesn't fit the shared tier1→tier2→tier3 shape.
        if (assetUri.StartsWith(AudioScheme, StringComparison.Ordinal))
        {
            return await TryResolveAudioAsync(assetUri[AudioScheme.Length..]);
        }

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
        // (populated by IModuleStore.LoadAsync — see LoadedModuleContent). Each IModuleAssetSource
        // resolves directly to whatever URI form suits its own platform (a data: URI on MAUI, a
        // blob: object URL on web) — this resolver doesn't need to know or care which.
        if (await TryResolveBundleLocalAsync(folder, slug, extensions) is { } url)
        {
            return url;
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

    private async Task<string?> TryResolveBundleLocalAsync(string folder, string slug, (string Ext, string MimeType)[] extensions)
    {
        foreach (var (ext, mime) in extensions)
        {
            var url = await sessionState.Assets.GetAssetUrlAsync($"assets/{folder}/{slug}{ext}", mime);
            if (url is not null)
            {
                return url;
            }
        }

        return null;
    }

    // audio:// resolves through bundle-local only — no dependency-pack placeholder (nothing
    // sensible stands in for real audio the way TestAssetPack's SVGs do for icons) and no
    // engine-fallback tier (unlike icon://'s generic fallback icon, there's no equivalent to "a
    // generic placeholder sound" that wouldn't be actively strange to hear); unresolved means
    // silence, matching image://'s/font://'s own no-fallback behavior.
    //
    // Culture-suffixed filenames (<slug>.<culture>.<ext>) are probed first, against
    // GameSessionState.Language — the same effective module-content locale already driving
    // .restext resolution — falling back to the bare <slug>{ext}, understood as the module's
    // default/authoring culture. No separate manifest field declares this: the absence of a
    // culture suffix *is* the declaration, mirroring how en-US.restext needs no explicit "this is
    // the default" marker beyond its own naming.
    private async Task<string?> TryResolveAudioAsync(string slug)
    {
        if (sessionState.Language is { } culture)
        {
            foreach (var (ext, mime) in AudioExtensions)
            {
                var url = await sessionState.Assets.GetAssetUrlAsync($"assets/audio/{slug}.{culture}{ext}", mime);
                if (url is not null)
                {
                    return url;
                }
            }
        }

        return await TryResolveBundleLocalAsync("audio", slug, AudioExtensions);
    }
}
