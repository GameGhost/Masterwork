using Masterwork.Engine;
using Masterwork.Engine.Audio;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// Bridges <see cref="GameSession.ResolveAudio"/>'s resolution logic and per-node SFX overrides to
/// <see cref="IAudioPlayer"/>, resolving every <c>audio://</c> URI through <see cref="IAssetResolver"/>
/// along the way — <see cref="GameSession"/>/<see cref="AudioResolver"/> only ever deal in raw
/// asset URIs, never playable URLs (that's this class's job, the same split
/// <see cref="Rendering.RenderedAudioTrackView"/> and <see cref="Rendering.RenderedImageView"/> already
/// draw between "what to play/show" and "how to fetch it"). Scoped (registered the same way as
/// <see cref="GameSessionState"/>), so <see cref="SyncMusicAsync"/>'s dedup cache lives for the app
/// session, not just one component.
/// </summary>
public sealed class AudioCoordinator(IAudioPlayer audioPlayer, IAssetResolver assetResolver)
{
    private AudioResolution? _lastPushed;

    /// <summary>
    /// Recomputes <paramref name="session"/>'s currently-effective background music and applies it,
    /// but only actually touches <see cref="IAudioPlayer"/> if the resolution differs from whatever
    /// was last pushed — repeated calls after actions that don't change the music stack (most
    /// passage renders) are then just a cheap comparison, not a redundant crossfade/restart. The
    /// dedup compares raw <see cref="AudioResolution"/> values (pre-resolve), not resolved URLs —
    /// cheaper, and equivalent, since the same URI always resolves the same way within one session.
    /// </summary>
    public async Task SyncMusicAsync(GameSession session)
    {
        var resolution = session.ResolveAudio();
        if (Equals(resolution, _lastPushed))
        {
            return;
        }

        _lastPushed = resolution;
        switch (resolution)
        {
            case AudioResolution.Silence:
                await audioPlayer.StopBgmAsync();
                break;
            case AudioResolution.SingleTrack single:
                var url = await assetResolver.ResolveAsync(single.Url);
                await audioPlayer.PlayBgmAsync(url);
                break;
            case AudioResolution.ModulePlaylist playlist:
                var urls = await ResolveAllAsync(playlist.Tracks);
                await audioPlayer.PlayBgmPlaylistAsync(urls, playlist.Order);
                break;
        }
    }

    // Drops any track that fails to resolve (a typo'd/missing asset) rather than failing the whole
    // playlist — matches image://'s own per-reference "unresolved just means this one thing is
    // missing" behavior. If every track fails, the empty list reaches PlayBgmPlaylistAsync, which
    // itself treats [] as silence (see its own remarks) — consistent with a single unresolved
    // SingleTrack's url becoming null above and PlayBgmAsync(null) already being a no-op.
    private async Task<IReadOnlyList<string>> ResolveAllAsync(IReadOnlyList<string> uris)
    {
        var resolved = new List<string>(uris.Count);
        foreach (var uri in uris)
        {
            if (await assetResolver.ResolveAsync(uri) is { } url)
            {
                resolved.Add(url);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Clears the cached "currently playing" resolution, so the next <see cref="SyncMusicAsync"/>
    /// call can't wrongly skip a real change. Call whenever background music may have changed
    /// outside this coordinator's own knowledge — specifically, every fresh <c>Play</c> mount: theme
    /// audio (a separate, non-stacked concern — see <c>docs/mws-format-latest.md</c> §6) may have
    /// been playing moments ago and was stopped independently before navigating in, but this
    /// coordinator has no way to observe that on its own.
    /// </summary>
    public void Reset() => _lastPushed = null;

    /// <summary>
    /// Resolves and plays a one-shot SFX using the flat 2-level check described in
    /// <see cref="AudioResolver"/>'s own remarks: <paramref name="nodeOverride"/> wins if present
    /// (present-but-empty means explicit silence, no module fallback); otherwise one entry from
    /// <paramref name="moduleBucket"/> is picked at random; otherwise nothing plays. A positive
    /// <paramref name="delayMs"/> is applied without blocking the caller (fire-and-forget), so a
    /// delayed SFX never holds up navigation or an engine call — the asset is resolved to a playable
    /// URL up front regardless, so the delay itself is pure wall-clock wait, not resolve time.
    /// </summary>
    public async Task PlaySfxAsync(string? nodeOverride, IReadOnlyList<string>? moduleBucket, int? delayMs)
    {
        var uri = ResolveSfxCandidate(nodeOverride, moduleBucket);
        if (uri is null)
        {
            return;
        }

        var url = await assetResolver.ResolveAsync(uri);
        if (url is null)
        {
            return;
        }

        if (delayMs is > 0)
        {
            _ = PlayAfterDelayAsync(url, delayMs.Value);
            return;
        }

        await audioPlayer.PlaySfxAsync(url);
    }

    private async Task PlayAfterDelayAsync(string url, int delayMs)
    {
        await Task.Delay(delayMs);
        await audioPlayer.PlaySfxAsync(url);
    }

    private static string? ResolveSfxCandidate(string? nodeOverride, IReadOnlyList<string>? moduleBucket)
    {
        if (nodeOverride is not null)
        {
            return nodeOverride.Length == 0 ? null : nodeOverride;
        }

        if (moduleBucket is null || moduleBucket.Count == 0)
        {
            return null;
        }

        return moduleBucket[Random.Shared.Next(moduleBucket.Count)];
    }
}
