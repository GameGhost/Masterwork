using Masterwork.App.Shared.Services;
using Microsoft.JSInterop;

namespace Masterwork.Tests;

public class AudioCoordinatorTests
{
    // Pass-through — every URI resolves to itself, so assertions can compare directly against the
    // URI passed into PlaySfxAsync without an extra resolution-mapping layer to reason about.
    private sealed class FakeAssetResolver : IAssetResolver
    {
        public Task<string?> ResolveAsync(string assetUri) => Task.FromResult<string?>(assetUri);
    }

    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public List<string> PlayedSfx { get; } = [];

        public Task PlaySfxAsync(string? url)
        {
            if (url is not null)
            {
                PlayedSfx.Add(url);
            }

            return Task.CompletedTask;
        }

        public Task PlayBgmAsync(string? url) => Task.CompletedTask;
        public Task PlayBgmPlaylistAsync(IReadOnlyList<string> urls, string order) => Task.CompletedTask;
        public Task StopBgmAsync() => Task.CompletedTask;
        public Task SetAmbientOnlyAsync(bool ambientOnly) => Task.CompletedTask;
        public Task SetBgmVolumeAsync(double volume) => Task.CompletedTask;
        public Task SetBgmMutedAsync(bool muted) => Task.CompletedTask;
        public Task SetSfxVolumeAsync(double volume) => Task.CompletedTask;
        public Task SetSfxMutedAsync(bool muted) => Task.CompletedTask;
        public Task LoadTrackAsync(string handle, string url, string bgmBehavior) => Task.CompletedTask;
        public Task PlayTrackAsync(string handle) => Task.CompletedTask;
        public Task PauseTrackAsync(string handle) => Task.CompletedTask;
        public Task SeekTrackAsync(string handle, double seconds) => Task.CompletedTask;
        public Task<TrackStatus> GetTrackStatusAsync(string handle) => Task.FromResult(new TrackStatus(false, 0, 0));
        public Task DisposeTrackAsync(string handle) => Task.CompletedTask;

        public Task SetTrackEndedCallbackAsync<T>(string handle, DotNetObjectReference<T> callbackTarget, string callbackMethodName) where T : class =>
            Task.CompletedTask;
    }

    private sealed class FakeMainMenuTheme(string? clickSfxUrl = "audio://theme/click") : IMainMenuTheme
    {
        public Type SceneComponentType => typeof(object);
        public string? MusicUrl => null;
        public string? TransitionSfxUrl => null;
        public string? ClickSfxUrl { get; } = clickSfxUrl;
    }

    private static (AudioCoordinator Coordinator, FakeAudioPlayer Player) CreateCoordinator(string? themeClickSfxUrl = "audio://theme/click")
    {
        var player = new FakeAudioPlayer();
        return (new AudioCoordinator(player, new FakeAssetResolver(), new FakeMainMenuTheme(themeClickSfxUrl)), player);
    }

    // ── 3-tier PlaySfxAsync(node, nodeDelay, layout, layoutDelay, moduleBucket) ────────────────

    [Fact]
    public async Task ThreeTier_NodeOverride_WinsOverLayoutAndModule()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync("audio://sfx/node", null, "audio://sfx/layout", null, ["audio://sfx/module"]);

        Assert.Equal(["audio://sfx/node"], player.PlayedSfx);
    }

    [Fact]
    public async Task ThreeTier_NodeAbsent_LayoutOverride_WinsOverModule()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync(null, null, "audio://sfx/layout", null, ["audio://sfx/module"]);

        Assert.Equal(["audio://sfx/layout"], player.PlayedSfx);
    }

    [Fact]
    public async Task ThreeTier_NodeAndLayoutAbsent_FallsThroughToModuleBucket()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync(null, null, null, null, ["audio://sfx/module"]);

        Assert.Equal(["audio://sfx/module"], player.PlayedSfx);
    }

    [Fact]
    public async Task ThreeTier_NodeEmpty_MeansExplicitSilence_NoFallbackToLayoutOrModule()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync("", null, "audio://sfx/layout", null, ["audio://sfx/module"]);

        Assert.Empty(player.PlayedSfx);
    }

    [Fact]
    public async Task ThreeTier_LayoutEmpty_MeansExplicitSilence_NoFallbackToModule()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync(null, null, "", null, ["audio://sfx/module"]);

        Assert.Empty(player.PlayedSfx);
    }

    [Fact]
    public async Task ThreeTier_NoTierHasACandidate_NothingPlays()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync(null, null, null, null, null);

        Assert.Empty(player.PlayedSfx);
    }

    // ── Delay must travel with whichever tier's URI actually wins ──────────────────────────────

    // Polls rather than a single fixed sleep — a fire-and-forget delayed play is scheduled onto the
    // thread pool, and a single short Task.Delay proved flaky under real test-process contention.
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task ThreeTier_LayoutWins_UsesLayoutDelayNotNodeDelay()
    {
        // Node is absent (so layout wins) but still carries a non-null delay of its own — a
        // situation that shouldn't arise from real authored content, but exercises the exact
        // correctness trap the 3-tier resolver has to avoid: the *losing* tier's delay must never
        // leak onto the *winning* tier's playback.
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync(null, 999_999, "audio://sfx/layout", 10, ["audio://sfx/module"]);
        Assert.Empty(player.PlayedSfx); // not yet — the winning (layout) tier's own delay hasn't elapsed

        await WaitUntilAsync(() => player.PlayedSfx.Count > 0);
        Assert.Equal(["audio://sfx/layout"], player.PlayedSfx);
    }

    [Fact]
    public async Task ThreeTier_NodeWins_UsesNodeDelayNotLayoutDelay()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync("audio://sfx/node", 10, "audio://sfx/layout", 999_999, ["audio://sfx/module"]);
        Assert.Empty(player.PlayedSfx);

        await WaitUntilAsync(() => player.PlayedSfx.Count > 0);
        Assert.Equal(["audio://sfx/node"], player.PlayedSfx);
    }

    // ── 2-tier overload (RenderedLinkView's click SFX) is unaffected ────────────────────────────

    [Fact]
    public async Task TwoTier_NodeOverride_WinsOverModule()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync("audio://sfx/node", ["audio://sfx/module"], null);

        Assert.Equal(["audio://sfx/node"], player.PlayedSfx);
    }

    [Fact]
    public async Task TwoTier_NodeAbsent_FallsThroughToModuleBucket()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlaySfxAsync(null, ["audio://sfx/module"], null);

        Assert.Equal(["audio://sfx/module"], player.PlayedSfx);
    }

    // ── PlayChromeClickAsync (app-chrome buttons) ───────────────────────────────────────────────

    [Fact]
    public async Task ChromeClick_NoModuleBucket_FallsBackToThemeClick()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlayChromeClickAsync(null);

        Assert.Equal(["audio://theme/click"], player.PlayedSfx);
    }

    [Fact]
    public async Task ChromeClick_ModuleBucketPresent_OverridesTheme()
    {
        var (coordinator, player) = CreateCoordinator();

        await coordinator.PlayChromeClickAsync(["audio://sfx/module-click"]);

        Assert.Equal(["audio://sfx/module-click"], player.PlayedSfx);
    }

    [Fact]
    public async Task ChromeClick_NoModuleBucketAndNoThemeClick_NothingPlays()
    {
        var (coordinator, player) = CreateCoordinator(themeClickSfxUrl: null);

        await coordinator.PlayChromeClickAsync(null);

        Assert.Empty(player.PlayedSfx);
    }
}
