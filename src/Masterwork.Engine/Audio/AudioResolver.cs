using Masterwork.Engine.Rendering;
using Masterwork.ModuleFormat;

namespace Masterwork.Engine.Audio;

/// <summary>
/// Resolves the currently-effective background music per the "topmost active element wins" stack
/// documented in <c>docs/mws-format-latest.md</c> §6 (Audio): walk outward from the deepest
/// currently-open popup, through any enclosing popups, to the current passage, to the module
/// default — the first tier with a <em>present</em> <c>audio.music</c> value wins (empty = silence,
/// stop; non-empty = that value, stop; absent = check the next tier out).
/// </summary>
/// <remarks>
/// SFX resolution (<c>on_display</c>/<c>click</c>/<c>open</c>/<c>okay</c>/<c>cancel</c>) is
/// deliberately NOT owned by this class — it's a much cheaper flat 2-level check (node override,
/// else module bucket, else silence) with no stack to walk, inlined at whichever firing site
/// actually needs it instead.
/// </remarks>
public static class AudioResolver
{
    /// <summary>
    /// Resolves the effective background music for <paramref name="render"/>, given which of its
    /// popups are currently open (<see cref="Masterwork.Engine.Session.SessionViewState.ExpandedPopups"/>)
    /// and the module's own default (<paramref name="moduleAudio"/>, <see langword="null"/> if the
    /// module declares none).
    /// </summary>
    public static AudioResolution ResolveMusic(
        PassageRenderResult render, IReadOnlySet<string> expandedPopupIds, ModuleAudioManifest? moduleAudio)
    {
        var popupMusic = FindTopmostOpenPopupMusic(render.Actions, expandedPopupIds);
        if (popupMusic is not null)
        {
            return ToResolution(popupMusic);
        }

        if (render.Music is not null)
        {
            return ToResolution(render.Music);
        }

        var tracks = moduleAudio?.Music?.DefaultTracks ?? [];
        return tracks.Count switch
        {
            0 => new AudioResolution.Silence(),
            1 => new AudioResolution.SingleTrack(tracks[0]),
            _ => new AudioResolution.ModulePlaylist(tracks, moduleAudio!.Music!.Order),
        };
    }

    private static AudioResolution ToResolution(string musicValue) =>
        musicValue.Length == 0 ? new AudioResolution.Silence() : new AudioResolution.SingleTrack(musicValue);

    // Depth-first, document order. A popup's own Actions only participate once that popup's Id is in
    // expandedPopupIds — its content is rendered eagerly regardless of whether it's actually open
    // (see RenderedPopup's remarks), so membership in expandedPopupIds is the only signal for "is
    // this actually showing right now." Recurses into a popup's own nested popups first (deeper =
    // more topmost = wins) before falling back to that popup's own audio.music. expandedPopupIds has
    // no ordering of its own (a plain HashSet<string>) — nesting depth can only come from this tree
    // walk, not from iterating the set.
    private static string? FindTopmostOpenPopupMusic(IReadOnlyList<RenderedAction> actions, IReadOnlySet<string> expandedPopupIds)
    {
        foreach (var action in actions)
        {
            if (action is not RenderedPopup popup || !expandedPopupIds.Contains(popup.Id))
            {
                continue;
            }

            var nested = FindTopmostOpenPopupMusic(popup.Actions, expandedPopupIds);
            if (nested is not null)
            {
                return nested;
            }

            if (popup.Audio?.Music is not null)
            {
                return popup.Audio.Music;
            }
        }

        return null;
    }
}
