using System.Text;
using Masterwork.App.Shared.Services;
using Microsoft.Extensions.Logging;

namespace Masterwork.Tests;

public class AssetResolverTests
{
    // Minimal capturing ILogger — just enough to assert a warning was (or wasn't) logged, without
    // pulling in a mocking library this test project doesn't otherwise use.
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

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

    private static GameSessionState MakeState(
        IReadOnlyDictionary<string, byte[]>? bundleLocal = null,
        IReadOnlyList<IReadOnlyDictionary<string, byte[]>>? dependencies = null,
        string? language = null)
    {
        var state = new GameSessionState();
        state.Start("m", "1.0.0", language,
            new LoadedModuleContent(
                Module: null!,
                Assets: new DictionaryModuleAssetSource(bundleLocal ?? new Dictionary<string, byte[]>()),
                DependencyAssets: [.. (dependencies ?? []).Select(d => (IModuleAssetSource)new DictionaryModuleAssetSource(d))],
                StyleCss: null),
            session: null!,
            voice: null);
        return state;
    }

    private static readonly AssetResolver Resolver = new(new GameSessionState());

    [Fact]
    public async Task UnknownSlug_FallsBackToEngineIcon()
    {
        var url = await Resolver.ResolveAsync("icon://nonexistent_test_icon");
        Assert.Equal("_content/Masterwork.App.Shared/assets/fallback-icon.svg", url);
    }

    [Fact]
    public async Task UnresolvedImageScheme_ReturnsNull()
    {
        // image:// has no engine-fallback tier — only bundle-local then dependency-pack. With no
        // module loaded (empty GameSessionState) and no dependencies, there's nothing to resolve.
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
        var state = MakeState(bundleLocal: new Dictionary<string, byte[]> { ["assets/icons/village.png"] = [1, 2, 3, 4] });
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String([1, 2, 3, 4])}", url);
    }

    [Fact]
    public async Task BundleLocalImage_ResolvesToDataUri()
    {
        var bytes = Encoding.UTF8.GetBytes("<svg></svg>");
        var state = MakeState(bundleLocal: new Dictionary<string, byte[]> { ["assets/images/MFW_Scenario_1.svg"] = bytes });
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
        var state = MakeState(bundleLocal: new Dictionary<string, byte[]> { ["assets/images/setup/StorybookToken.png"] = bytes });
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("image://setup/StorybookToken");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task UnresolvedFontScheme_ReturnsNull()
    {
        var url = await Resolver.ResolveAsync("font://averia-libre-regular");
        Assert.Null(url);
    }

    [Fact]
    public async Task BundleLocalFont_ResolvesToDataUri()
    {
        var bytes = new byte[] { 5, 6, 7, 8 };
        var state = MakeState(bundleLocal: new Dictionary<string, byte[]> { ["assets/fonts/averia-libre-regular.woff2"] = bytes });
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("font://averia-libre-regular");

        Assert.Equal($"data:font/woff2;base64,{Convert.ToBase64String(bytes)}", url);
    }

    // ── dependency-pack tier (icon/image/font) ──────────────────────────────

    [Fact]
    public async Task DependencyPackIcon_ResolvesWhenNotInBundleLocal()
    {
        var bytes = new byte[] { 1, 1, 1 };
        var state = MakeState(dependencies: [new Dictionary<string, byte[]> { ["assets/icons/village.png"] = bytes }]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task DependencyPackImage_ResolvesWhenNotInBundleLocal()
    {
        var bytes = new byte[] { 2, 2, 2 };
        var state = MakeState(dependencies: [new Dictionary<string, byte[]> { ["assets/images/backgrounds/leather_large.png"] = bytes }]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("image://backgrounds/leather_large");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task DependencyPackFont_ResolvesWhenNotInBundleLocal()
    {
        var bytes = new byte[] { 3, 3, 3 };
        var state = MakeState(dependencies: [new Dictionary<string, byte[]> { ["assets/fonts/averia-libre-regular.woff2"] = bytes }]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("font://averia-libre-regular");

        Assert.Equal($"data:font/woff2;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocal_TakesPrecedenceOverDependencyPack()
    {
        var bundleBytes = new byte[] { 9 };
        var dependencyBytes = new byte[] { 8 };
        var state = MakeState(
            bundleLocal: new Dictionary<string, byte[]> { ["assets/icons/village.png"] = bundleBytes },
            dependencies: [new Dictionary<string, byte[]> { ["assets/icons/village.png"] = dependencyBytes }]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(bundleBytes)}", url);
    }

    [Fact]
    public async Task EarlierDependency_TakesPrecedenceOverLaterDependency()
    {
        var firstBytes = new byte[] { 1 };
        var secondBytes = new byte[] { 2 };
        var state = MakeState(dependencies:
        [
            new Dictionary<string, byte[]> { ["assets/icons/village.png"] = firstBytes },
            new Dictionary<string, byte[]> { ["assets/icons/village.png"] = secondBytes },
        ]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://village");

        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(firstBytes)}", url);
    }

    [Fact]
    public async Task IconUnresolvedInBundleOrDependencyPack_FallsBackToEngineIcon()
    {
        var state = MakeState(dependencies: [new Dictionary<string, byte[]>()]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("icon://nonexistent_test_icon");

        Assert.Equal("_content/Masterwork.App.Shared/assets/fallback-icon.svg", url);
    }

    // ── audio:// ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnresolvedAudioScheme_ReturnsNull()
    {
        var url = await Resolver.ResolveAsync("audio://bgm/theme");
        Assert.Null(url);
    }

    [Fact]
    public async Task BundleLocalAudio_ResolvesToDataUri()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var state = MakeState(bundleLocal: new Dictionary<string, byte[]> { ["assets/audio/bgm/theme.mp3"] = bytes });
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://bgm/theme");

        Assert.Equal($"data:audio/mpeg;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task DependencyPackAudio_ResolvesWhenNotInBundleLocal()
    {
        // The real-world case this covers: bgm/sfx live in a shared asset pack (e.g.
        // mwf-common-assets) now, not the module's own bundle.
        var bytes = new byte[] { 3, 3, 3 };
        var state = MakeState(dependencies: [new Dictionary<string, byte[]> { ["assets/audio/sfx/click.ogg"] = bytes }]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://sfx/click");

        Assert.Equal($"data:audio/ogg;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task BundleLocalAudio_TakesPrecedenceOverDependencyPack()
    {
        var bundleBytes = new byte[] { 6, 6, 6 };
        var dependencyBytes = new byte[] { 7, 7, 7 };
        var state = MakeState(
            bundleLocal: new Dictionary<string, byte[]> { ["assets/audio/sfx/click.ogg"] = bundleBytes },
            dependencies: [new Dictionary<string, byte[]> { ["assets/audio/sfx/click.ogg"] = dependencyBytes }]);
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://sfx/click");

        Assert.Equal($"data:audio/ogg;base64,{Convert.ToBase64String(bundleBytes)}", url);
    }

    [Fact]
    public async Task Audio_CultureSuffixedFile_IsNotPreferred()
    {
        // The general <slug>.<culture> lookup is gone: voice language is chosen by voice track, never by text language.
        var cultureBytes = new byte[] { 7, 7, 7 };
        var bareBytes = new byte[] { 8, 8, 8 };
        var state = MakeState(
            bundleLocal: new Dictionary<string, byte[]>
            {
                ["assets/audio/sfx/click.fr-CA.ogg"] = cultureBytes,
                ["assets/audio/sfx/click.ogg"] = bareBytes,
            },
            language: "fr-CA");
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://sfx/click");

        Assert.Equal($"data:audio/ogg;base64,{Convert.ToBase64String(bareBytes)}", url);
    }

    // ── audio://vo/ — narration from the selected voice track ───────────────

    private static AvailableVoiceTrack Voice(string language, string id, IReadOnlyDictionary<string, byte[]> files) =>
        new(new Masterwork.ModuleFormat.VoiceTrack { Language = language, Id = id }, new DictionaryModuleAssetSource(files));

    [Fact]
    public async Task Narration_ResolvesFromTheSelectedTracksFolder()
    {
        var female = new byte[] { 1, 1 };
        var male = new byte[] { 2, 2 };
        var files = new Dictionary<string, byte[]>
        {
            ["assets/audio/vo/en-US/female/greeting.ogg"] = female,
            ["assets/audio/vo/en-US/male/greeting.ogg"] = male,
        };
        var state = MakeState(bundleLocal: files);
        state.SetVoices([Voice("en-US", "male", files), Voice("en-US", "female", files)], "en-US", "female");
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://vo/greeting");

        Assert.Equal($"data:audio/ogg;base64,{Convert.ToBase64String(female)}", url);
    }

    [Fact]
    public async Task Narration_ResolvesFromAVoicePacksSource()
    {
        var bytes = new byte[] { 3, 3 };
        var state = MakeState();
        state.SetVoices([Voice("cs-CZ", "reader", new Dictionary<string, byte[]> { ["assets/audio/vo/cs-CZ/reader/greeting.mp3"] = bytes })], "cs-CZ", "reader");
        var resolver = new AssetResolver(state);

        var url = await resolver.ResolveAsync("audio://vo/greeting");

        Assert.Equal($"data:audio/mpeg;base64,{Convert.ToBase64String(bytes)}", url);
    }

    [Fact]
    public async Task Narration_MissingFromTheSelectedTrack_NeverBorrowsAnotherTrack_AndLogsOnce()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["assets/audio/vo/en-US/male/greeting.ogg"] = [1],
            ["assets/audio/vo/greeting.ogg"] = [2],
        };
        var state = MakeState(bundleLocal: files);
        state.SetVoices([Voice("en-US", "male", files), Voice("en-US", "female", files)], "en-US", "female");
        var log = new CapturingLogger<AssetResolver>();
        var resolver = new AssetResolver(state, log);

        Assert.Null(await resolver.ResolveAsync("audio://vo/greeting"));
        Assert.Null(await resolver.ResolveAsync("audio://vo/greeting"));

        var entry = Assert.Single(log.Entries, e => e.Level >= LogLevel.Information);
        Assert.Contains("greeting", entry.Message);
        Assert.Contains("female", entry.Message);
    }

    [Fact]
    public async Task Narration_UnderNone_ResolvesToNothing_WithoutAWarning()
    {
        var files = new Dictionary<string, byte[]> { ["assets/audio/vo/en-US/male/greeting.ogg"] = [1] };
        var state = MakeState(bundleLocal: files);
        state.SetVoices([Voice("en-US", "male", files)], null, null);
        var log = new CapturingLogger<AssetResolver>();
        var resolver = new AssetResolver(state, log);

        Assert.Null(await resolver.ResolveAsync("audio://vo/greeting"));
        Assert.DoesNotContain(log.Entries, e => e.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task Narration_ChangingTheTrack_ReResolves()
    {
        var female = new byte[] { 1 };
        var male = new byte[] { 2 };
        var files = new Dictionary<string, byte[]>
        {
            ["assets/audio/vo/en-US/female/greeting.ogg"] = female,
            ["assets/audio/vo/en-US/male/greeting.ogg"] = male,
        };
        var state = MakeState(bundleLocal: files);
        state.SetVoices([Voice("en-US", "male", files), Voice("en-US", "female", files)], "en-US", "male");
        var resolver = new AssetResolver(state);
        Assert.Equal($"data:audio/ogg;base64,{Convert.ToBase64String(male)}", await resolver.ResolveAsync("audio://vo/greeting"));

        state.SetVoices(state.Voices, "en-US", "female");

        Assert.Equal($"data:audio/ogg;base64,{Convert.ToBase64String(female)}", await resolver.ResolveAsync("audio://vo/greeting"));
    }

    [Fact]
    public async Task UnresolvedAudio_LogsWarningWithSlug()
    {
        var log = new CapturingLogger<AssetResolver>();
        var resolver = new AssetResolver(new GameSessionState(), log);

        var url = await resolver.ResolveAsync("audio://sfx/missing_click");

        Assert.Null(url);
        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("missing_click", warning.Message);
    }

    [Fact]
    public async Task ResolvedAudio_LogsNoWarning()
    {
        var log = new CapturingLogger<AssetResolver>();
        var bytes = new byte[] { 1, 2, 3 };
        var state = MakeState(bundleLocal: new Dictionary<string, byte[]> { ["assets/audio/sfx/click.ogg"] = bytes });
        var resolver = new AssetResolver(state, log);

        var url = await resolver.ResolveAsync("audio://sfx/click");

        Assert.NotNull(url);
        Assert.DoesNotContain(log.Entries, e => e.Level == LogLevel.Warning);
    }
}
