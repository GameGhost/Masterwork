using System.Text;
using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class AssetResolverTests
{
    // Minimal in-memory IModuleAssetSource, same shape as LoadedModuleContentTests's own adapter —
    // just enough to drive AssetResolver against a fixed set of bytes without a real
    // IndexedDB/filesystem-backed implementation.
    private sealed class DictionaryModuleAssetSource(IReadOnlyDictionary<string, byte[]> assets) : IModuleAssetSource
    {
        public Task<byte[]?> GetAssetAsync(string assetPath) =>
            Task.FromResult(assets.TryGetValue(assetPath, out var bytes) ? bytes : null);

        public Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) =>
            Task.FromResult(assets.TryGetValue(assetPath, out var bytes)
                ? $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}"
                : null);

        public Task<IReadOnlyList<string>> ListAssetPathsAsync() =>
            Task.FromResult<IReadOnlyList<string>>([.. assets.Keys]);
    }

    private static readonly AssetResolver Resolver = new(new GameSessionState());

    [Fact]
    public async Task KnownSlug_ResolvesToTestPackUrl()
    {
        var url = await Resolver.ResolveAsync("icon://village");
        Assert.Equal("_content/Masterwork.App.Shared/assets/test-pack/village.svg", url);
    }

    [Fact]
    public async Task UnknownSlug_FallsBackToEngineIcon()
    {
        var url = await Resolver.ResolveAsync("icon://nonexistent_test_icon");
        Assert.Equal("_content/Masterwork.App.Shared/assets/fallback-icon.svg", url);
    }

    [Fact]
    public async Task UnresolvedImageScheme_ReturnsNull()
    {
        // image:// has no dependency-pack/fallback tier — only bundle-local. With no module
        // loaded (empty GameSessionState), there's nothing to resolve against.
        var url = await Resolver.ResolveAsync("image://something");
        Assert.Null(url);
    }

    [Fact]
    public async Task UnsupportedScheme_ReturnsNull()
    {
        var url = await Resolver.ResolveAsync("synth://tone");
        Assert.Null(url);
    }

    [Fact]
    public async Task BundleLocalIcon_ResolvesToDataUri()
    {
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/icons/village.png"] = [1, 2, 3, 4] }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String([1, 2, 3, 4])}", url);
    }

    [Fact]
    public async Task BundleLocalImage_ResolvesToDataUri()
    {
        var bytes = Encoding.UTF8.GetBytes("<svg></svg>");
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/images/MFW_Scenario_1.svg"] = bytes }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("image://MFW_Scenario_1");

        Assert.Equal($"data:image/svg+xml;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocalImage_SubpathSlug_ResolvesToDataUri()
    {
        // image://setup/StorybookToken (a subpath slug) should resolve the same way as a flat
        // slug — the lookup key is built by plain concatenation ($"assets/{folder}/{slug}{ext}"),
        // so a slug containing '/' just addresses a nested asset path with no special handling.
        var bytes = Encoding.UTF8.GetBytes("fake png bytes");
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/images/setup/StorybookToken.png"] = bytes }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("image://setup/StorybookToken");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task UnresolvedFontScheme_ReturnsNull()
    {
        // font:// has no dependency-pack/fallback tier either — only bundle-local.
        var url = await Resolver.ResolveAsync("font://averia-libre-regular");
        Assert.Null(url);
    }

    [Fact]
    public async Task BundleLocalFont_ResolvesToDataUri()
    {
        var bytes = new byte[] { 5, 6, 7, 8 };
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/fonts/averia-libre-regular.woff2"] = bytes }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("font://averia-libre-regular");

        Assert.Equal($"data:font/woff2;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocalTakesPrecedenceOverTestPack()
    {
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/icons/village.png"] = [9] }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String([9])}", url);
    }

    // ── audio:// ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnresolvedAudioScheme_ReturnsNull()
    {
        // audio:// has no dependency-pack/fallback tier either — only bundle-local. With no module
        // loaded (empty GameSessionState), there's nothing to resolve against.
        var url = await Resolver.ResolveAsync("audio://bgm/theme");
        Assert.Null(url);
    }

    [Fact]
    public async Task BundleLocalAudio_ResolvesToDataUri()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/audio/bgm/theme.mp3"] = bytes }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://bgm/theme");

        Assert.Equal($"data:audio/mpeg;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocalAudio_SubpathSlug_ResolvesToDataUri()
    {
        // audio://[<path>/]<slug> — bgm/sfx/vo are folder-naming conventions within the one scheme,
        // not special-cased by the resolver; a multi-segment slug just addresses a nested path, the
        // same as image://'s own subpath-slug support.
        var bytes = new byte[] { 4, 5, 6 };
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/audio/vo/greeting.ogg"] = bytes }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://vo/greeting");

        Assert.Equal($"data:audio/ogg;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocalAudio_CultureSuffixedFileExists_PreferredOverBare()
    {
        var cultureBytes = new byte[] { 7, 7, 7 };
        var bareBytes = new byte[] { 8, 8, 8 };
        var state = new GameSessionState();
        state.Start("m", "1.0.0", "fr-CA",
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]>
                {
                    ["assets/audio/vo/battletime_narration.fr-CA.mp3"] = cultureBytes,
                    ["assets/audio/vo/battletime_narration.mp3"] = bareBytes,
                }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://vo/battletime_narration");

        Assert.Equal($"data:audio/mpeg;base64,{Convert.ToBase64String(cultureBytes)}", url);
    }

    [Fact]
    public async Task BundleLocalAudio_NoCultureSuffixedFile_FallsBackToBare()
    {
        var bareBytes = new byte[] { 9, 9, 9 };
        var state = new GameSessionState();
        state.Start("m", "1.0.0", "fr-CA",
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]>
                {
                    ["assets/audio/vo/battletime_narration.mp3"] = bareBytes,
                }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://vo/battletime_narration");

        Assert.Equal($"data:audio/mpeg;base64,{Convert.ToBase64String(bareBytes)}", url);
    }

    [Fact]
    public async Task BundleLocalAudio_NoSessionLanguage_SkipsCultureProbe_ResolvesBare()
    {
        var bareBytes = new byte[] { 2, 2, 2 };
        var state = new GameSessionState();
        state.Start("m", "1.0.0", null,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(new Dictionary<string, byte[]> { ["assets/audio/vo/greeting.mp3"] = bareBytes }),
                StyleCss: null),
            session: null!);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://vo/greeting");

        Assert.Equal($"data:audio/mpeg;base64,{Convert.ToBase64String(bareBytes)}", url);
    }
}
