using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.App.Shared.Services;

/// <inheritdoc cref="IAssetResolver"/>
public sealed class AssetResolver(GameSessionState sessionState, ILogger<AssetResolver>? logger = null) : IAssetResolver
{
    private readonly ILogger<AssetResolver> _logger = logger ?? NullLogger<AssetResolver>.Instance;

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
    // The selected voice track is part of the same key: audio://vo/ resolves against it.
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private IModuleAssetSource? _cachedForAssets;
    private AvailableVoiceTrack? _cachedForVoice;

    private const string NarrationPrefix = "vo/";

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
        if (!ReferenceEquals(_cachedForAssets, sessionState.Assets) || !ReferenceEquals(_cachedForVoice, sessionState.Voice))
        {
            _cache.Clear();
            _cachedForAssets = sessionState.Assets;
            _cachedForVoice = sessionState.Voice;
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
        // audio:// has its own path: narration (vo/) resolves only against the selected voice track,
        // and nothing audio has an engine-fallback tier.
        if (assetUri.StartsWith(AudioScheme, StringComparison.Ordinal))
        {
            var audioSlug = assetUri[AudioScheme.Length..];
            return audioSlug.StartsWith(NarrationPrefix, StringComparison.Ordinal)
                ? await TryResolveNarrationAsync(audioSlug[NarrationPrefix.Length..])
                : await TryResolveAudioAsync(audioSlug);
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
        if (await TryResolveFromSourceAsync(sessionState.Assets, folder, slug, extensions) is { } url)
        {
            return url;
        }

        // Tier 2 (dependency pack): each declared asset-pack dependency's own assets, in
        // declaration order — first match wins, same as bundle-local above. This is how a module
        // that no longer ships its own style/icons/images (moved into a shared pack, e.g.
        // mwf-common-assets) still resolves them.
        foreach (var dependency in sessionState.DependencyAssets)
        {
            if (await TryResolveFromSourceAsync(dependency, folder, slug, extensions) is { } dependencyUrl)
            {
                return dependencyUrl;
            }
        }

        // Tier 3 (engine fallback) — icon:// only; an unresolved image:// or font:// yields null
        // (the caller, e.g. RenderedImageView, shows its own "missing image" state; an unresolved
        // font:// in a @font-face rule just leaves that face unavailable, same as any 404'd font).
        return scheme == IconScheme ? FallbackIcon : null;
    }

    private static async Task<string?> TryResolveFromSourceAsync(IModuleAssetSource source, string folder, string slug, (string Ext, string MimeType)[] extensions)
    {
        foreach (var (ext, mime) in extensions)
        {
            var url = await source.GetAssetUrlAsync($"assets/{folder}/{slug}{ext}", mime);
            if (url is not null)
            {
                return url;
            }
        }

        return null;
    }

    // audio:// (other than vo/) tries bundle-local, then each dependency pack in turn (bgm/sfx
    // typically live in a shared pack). No engine-fallback tier: there's no generic placeholder
    // sound that wouldn't be strange to hear, so unresolved means silence.
    private async Task<string?> TryResolveAudioAsync(string slug)
    {
        if (await TryResolveFromSourceAsync(sessionState.Assets, "audio", slug, AudioExtensions) is { } bundleLocal)
        {
            return bundleLocal;
        }

        foreach (var dependency in sessionState.DependencyAssets)
        {
            if (await TryResolveFromSourceAsync(dependency, "audio", slug, AudioExtensions) is { } dependencyUrl)
            {
                return dependencyUrl;
            }
        }

        _logger.LogWarning("Could not resolve audio asset 'audio://{Slug}' — no matching file in the loaded module's or any dependency asset pack's assets/audio/", slug);
        return null;
    }

    // audio://vo/<slug> resolves only in the selected voice track's folder, from that track's own
    // source (the module or a voice pack). A track without the file is a gap: the caller hides the
    // audio_track. It never borrows another track's file.
    private async Task<string?> TryResolveNarrationAsync(string slug)
    {
        if (sessionState.Voice is not { } voice)
        {
            _logger.LogDebug("No voice track selected; narration 'audio://vo/{Slug}' is not played", slug);
            return null;
        }

        var track = voice.Track;
        foreach (var (ext, mime) in AudioExtensions)
        {
            if (await voice.Source.GetAssetUrlAsync($"{track.AssetFolder}/{slug}{ext}", mime) is { } url)
            {
                return url;
            }
        }

        _logger.LogInformation("Voice track {Language}/{Track} has no narration for 'audio://vo/{Slug}'; hiding it", track.Language, track.Id, slug);
        return null;
    }
}
