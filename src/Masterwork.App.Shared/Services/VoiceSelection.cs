using Masterwork.ModuleFormat;

namespace Masterwork.App.Shared.Services;

/// <summary>A voice track offered for the loaded module, with the asset source its files come from (the module's own, or a voice pack's).</summary>
public sealed record AvailableVoiceTrack(VoiceTrack Track, IModuleAssetSource Source);

/// <summary>
/// Picks a playthrough's text language, voice language and voice track from the player's settings
/// and what the module offers. Text and voice language are chosen independently; narration never
/// falls back silently to a language the player didn't ask for.
/// </summary>
public static class VoiceSelection
{
    /// <summary>The <see cref="AppSettings.VoiceLanguage"/> value meaning "no narration".</summary>
    public const string None = "none";

    /// <summary>
    /// The text language for a new playthrough: <paramref name="preferred"/>, else
    /// <paramref name="uiLocale"/>, else <paramref name="moduleDefault"/>, each only if
    /// <paramref name="available"/> has it; else the first available language.
    /// </summary>
    public static string SelectTextLanguage(IReadOnlyList<string> available, string? preferred, string? uiLocale, string moduleDefault)
    {
        foreach (var candidate in new[] { preferred, uiLocale, moduleDefault })
        {
            if (candidate is not null && available.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                return available.First(a => string.Equals(a, candidate, StringComparison.OrdinalIgnoreCase));
            }
        }

        return available.Count > 0 ? available[0] : moduleDefault;
    }

    /// <summary>
    /// The effective voice language, or <see langword="null"/> for None. An explicit
    /// <paramref name="preference"/> is used only if a track exists in it (else None); with no
    /// preference, <paramref name="textLanguage"/> is used only if a track exists in it (else None).
    /// Matching ignores case; the result is spelled as the tracks spell it.
    /// </summary>
    public static string? SelectVoiceLanguage(string? preference, string? textLanguage, IEnumerable<VoiceTrack> tracks)
    {
        if (preference == None)
        {
            return null;
        }

        var wanted = preference ?? textLanguage;
        return wanted is null
            ? null
            : tracks.FirstOrDefault(t => string.Equals(t.Language, wanted, StringComparison.OrdinalIgnoreCase))?.Language;
    }

    /// <summary>The track to play in <paramref name="voiceLanguage"/>: <paramref name="preferredTrackId"/> if that language has it, else its first track. <see langword="null"/> under None.</summary>
    public static VoiceTrack? SelectTrack(string? voiceLanguage, IEnumerable<VoiceTrack> tracks, string? preferredTrackId)
    {
        if (voiceLanguage is null)
        {
            return null;
        }

        var inLanguage = tracks.Where(t => t.Language == voiceLanguage).ToList();
        return inLanguage.FirstOrDefault(t => t.Id == preferredTrackId) ?? inLanguage.FirstOrDefault();
    }

    /// <summary>The track to play for <paramref name="moduleId"/>, from <paramref name="settings"/>, or <see langword="null"/> for no narration.</summary>
    public static VoiceTrack? Select(AppSettings settings, string moduleId, string? textLanguage, IReadOnlyList<VoiceTrack> tracks)
    {
        var language = SelectVoiceLanguage(settings.VoiceLanguage, textLanguage, tracks);
        return SelectTrack(language, tracks, PreferredTrackId(settings, moduleId, language));
    }

    /// <summary>The track id the player picked for <paramref name="moduleId"/> in <paramref name="language"/>, if any.</summary>
    public static string? PreferredTrackId(AppSettings settings, string moduleId, string? language) =>
        settings.VoiceTrackPreferences.LastOrDefault(p => p.ModuleId == moduleId && p.Language == language)?.TrackId;

    /// <summary>A copy of <paramref name="settings"/> remembering <paramref name="trackId"/> for <paramref name="moduleId"/> in <paramref name="language"/>.</summary>
    public static AppSettings WithTrackPreference(AppSettings settings, string moduleId, string language, string trackId) =>
        settings with
        {
            VoiceTrackPreferences =
            [
                .. settings.VoiceTrackPreferences.Where(p => !(p.ModuleId == moduleId && p.Language == language)),
                new VoiceTrackPreference(moduleId, language, trackId),
            ],
        };

    /// <summary>
    /// The tracks offered for a module at <paramref name="moduleVersion"/>: its own tracks first, then
    /// each installed pack's, in order. A pack needing a newer module is left out, and a track whose
    /// (language, id) is already offered is skipped, keeping the first.
    /// </summary>
    public static IReadOnlyList<AvailableVoiceTrack> Offered(
        string moduleVersion, IReadOnlyList<VoiceTrack> moduleTracks, IModuleAssetSource moduleAssets, IReadOnlyList<InstalledVoicePack> packs)
    {
        var offered = new List<AvailableVoiceTrack>();
        Add(moduleTracks, moduleAssets);
        foreach (var pack in packs.Where(p => SupportsModuleVersion(p.Manifest, moduleVersion)))
        {
            Add(pack.Manifest.Voices, pack.Assets);
        }

        return offered;

        void Add(IEnumerable<VoiceTrack> tracks, IModuleAssetSource source)
        {
            foreach (var track in tracks)
            {
                if (!offered.Any(o => string.Equals(o.Track.Language, track.Language, StringComparison.OrdinalIgnoreCase) && o.Track.Id == track.Id))
                {
                    offered.Add(new AvailableVoiceTrack(track, source));
                }
            }
        }
    }

    /// <summary>Whether <paramref name="pack"/> works with a module at <paramref name="moduleVersion"/>. Only the minimum is checked.</summary>
    public static bool SupportsModuleVersion(VoicePackManifest pack, string moduleVersion) =>
        SemVer.Compare(moduleVersion, pack.For.MinVersion) >= 0;
}
