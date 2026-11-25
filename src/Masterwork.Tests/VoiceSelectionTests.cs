using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class VoiceSelectionTests
{
    private static VoiceTrack Track(string language, string id) => new() { Language = language, Id = id };

    private static readonly IReadOnlyList<VoiceTrack> EnglishAndCzech =
        [Track("en-US", "male"), Track("en-US", "female"), Track("cs-CZ", "reader")];

    // ── Text language ───────────────────────────────────────────────────────

    [Fact]
    public void TextLanguage_ExplicitPreferenceTheModuleHas_Wins()
    {
        Assert.Equal("fr-CA", VoiceSelection.SelectTextLanguage(["en-US", "fr-CA"], preferred: "fr-CA", uiLocale: "en-US", moduleDefault: "en-US"));
    }

    [Fact]
    public void TextLanguage_PreferenceMissing_FallsBackToModuleDefaultNotUiLanguage()
    {
        Assert.Equal("en-US", VoiceSelection.SelectTextLanguage(["en-US", "fr-CA"], preferred: "de-DE", uiLocale: "fr-CA", moduleDefault: "en-US"));
    }

    [Fact]
    public void TextLanguage_SameAsApp_UsesUiLanguage()
    {
        Assert.Equal("fr-CA", VoiceSelection.SelectTextLanguage(["en-US", "fr-CA"], preferred: null, uiLocale: "fr-CA", moduleDefault: "en-US"));
    }

    [Fact]
    public void TextLanguage_SameAsAppAndUiLanguageMissing_FallsBackToModuleDefault()
    {
        Assert.Equal("fr-CA", VoiceSelection.SelectTextLanguage(["en-US", "fr-CA"], preferred: null, uiLocale: "de-DE", moduleDefault: "fr-CA"));
    }

    [Fact]
    public void TextLanguage_NothingMatches_FallsBackToFirstAvailable()
    {
        Assert.Equal("cs-CZ", VoiceSelection.SelectTextLanguage(["cs-CZ"], preferred: null, uiLocale: "en-US", moduleDefault: "en-US"));
    }

    // ── Voice language ──────────────────────────────────────────────────────

    private static string? Voice(string? preference, string? text, string? ui = "de-DE", string? moduleDefault = "de-DE") =>
        VoiceSelection.SelectVoiceLanguage(preference, text, ui, moduleDefault, EnglishAndCzech);

    [Fact]
    public void BestMatch_PrefersTheTextLanguage()
    {
        Assert.Equal("cs-CZ", Voice(preference: null, text: "cs-CZ", ui: "en-US", moduleDefault: "en-US"));
    }

    [Fact]
    public void BestMatch_NoTrackInTextLanguage_FallsBackToUiLanguage()
    {
        Assert.Equal("cs-CZ", Voice(preference: null, text: "fr-CA", ui: "cs-CZ", moduleDefault: "en-US"));
    }

    [Fact]
    public void BestMatch_NoTrackInTextOrUiLanguage_FallsBackToModuleDefault()
    {
        Assert.Equal("en-US", Voice(preference: null, text: "fr-CA", ui: "fr-CA", moduleDefault: "en-US"));
    }

    [Fact]
    public void BestMatch_NoTrackInAnyOfThem_IsNone()
    {
        Assert.Null(Voice(preference: null, text: "fr-CA", ui: "fr-CA", moduleDefault: "fr-CA"));
    }

    [Fact]
    public void SameAsText_FollowsTextLanguageWhenATrackExists()
    {
        Assert.Equal("cs-CZ", Voice(preference: VoiceSelection.SameAsText, text: "cs-CZ"));
    }

    [Fact]
    public void SameAsText_NoTrackInTextLanguage_IsNone()
    {
        // Unlike best match, never tries another language: French text with only English narration is None.
        Assert.Null(Voice(preference: VoiceSelection.SameAsText, text: "fr-CA", ui: "en-US", moduleDefault: "en-US"));
    }

    [Fact]
    public void ExplicitLanguageTheModuleHas_WinsOverTextLanguage()
    {
        Assert.Equal("en-US", Voice(preference: "en-US", text: "cs-CZ"));
    }

    [Fact]
    public void ExplicitLanguageTheModuleLacks_IsNone()
    {
        Assert.Null(Voice(preference: "fr-CA", text: "en-US", ui: "en-US", moduleDefault: "en-US"));
    }

    [Fact]
    public void VoiceLanguage_MatchesIgnoringCase_SpelledAsTheTracksSpellIt()
    {
        Assert.Equal("cs-CZ", Voice(preference: null, text: "CS-cz"));
    }

    [Fact]
    public void ExplicitNone_IsNone()
    {
        Assert.Null(Voice(preference: VoiceSelection.None, text: "en-US", ui: "en-US", moduleDefault: "en-US"));
    }

    // ── Track ───────────────────────────────────────────────────────────────

    [Fact]
    public void Track_NoPreference_IsTheFirstTrackForTheLanguage()
    {
        Assert.Equal("male", VoiceSelection.SelectTrack("en-US", EnglishAndCzech, preferredTrackId: null)?.Id);
    }

    [Fact]
    public void Track_PreferenceInTheLanguage_Wins()
    {
        Assert.Equal("female", VoiceSelection.SelectTrack("en-US", EnglishAndCzech, preferredTrackId: "female")?.Id);
    }

    [Fact]
    public void Track_PreferenceNotInTheLanguage_FallsBackToTheFirstTrack()
    {
        Assert.Equal("reader", VoiceSelection.SelectTrack("cs-CZ", EnglishAndCzech, preferredTrackId: "female")?.Id);
    }

    [Fact]
    public void Track_LanguageNone_IsNoTrack()
    {
        Assert.Null(VoiceSelection.SelectTrack(null, EnglishAndCzech, preferredTrackId: "male"));
    }

    [Fact]
    public void Select_ReadsTheModulesTrackPreferenceForTheEffectiveLanguage()
    {
        var settings = new AppSettings
        {
            VoiceTrackPreferences =
            [
                new VoiceTrackPreference("other.module", "en-US", "male"),
                new VoiceTrackPreference("this.module", "cs-CZ", "reader"),
                new VoiceTrackPreference("this.module", "en-US", "female"),
            ],
        };

        var track = VoiceSelection.Select(settings, "this.module", "en-US", "en-US", EnglishAndCzech);

        Assert.Equal(("en-US", "female"), (track?.Language, track?.Id));
    }

    [Fact]
    public void WithTrackPreference_ReplacesOnlyThatModuleAndLanguage()
    {
        var settings = new AppSettings
        {
            VoiceTrackPreferences =
            [
                new VoiceTrackPreference("this.module", "en-US", "male"),
                new VoiceTrackPreference("this.module", "cs-CZ", "reader"),
            ],
        };

        var updated = VoiceSelection.WithTrackPreference(settings, "this.module", "en-US", "female");

        Assert.Equal(
            [new VoiceTrackPreference("this.module", "cs-CZ", "reader"), new VoiceTrackPreference("this.module", "en-US", "female")],
            updated.VoiceTrackPreferences);
    }

    // ── Offered tracks: module + installed packs ────────────────────────────

    private sealed class NamedSource(string name) : IModuleAssetSource
    {
        public string Name => name;
        public Task<byte[]?> GetAssetAsync(string assetPath) => Task.FromResult<byte[]?>(null);
        public Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> ListAssetPathsAsync() => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private static InstalledVoicePack Pack(string id, string minVersion, params VoiceTrack[] tracks) =>
        new(new VoicePackManifest
        {
            Id = id,
            Title = id,
            Version = "1.0.0",
            For = new VoicePackTarget { ModuleId = "this.module", MinVersion = minVersion },
            Voices = tracks,
        }, new NamedSource(id));

    [Fact]
    public void Offered_ModuleTracksFirst_ThenEachPacksTracks()
    {
        var offered = VoiceSelection.Offered(
            "1.2.0", [Track("en-US", "male")], new NamedSource("module"),
            [Pack("pack.cs", "1.0.0", Track("cs-CZ", "reader"))]);

        Assert.Equal(
            [("en-US", "male", "module"), ("cs-CZ", "reader", "pack.cs")],
            offered.Select(o => (o.Track.Language, o.Track.Id, ((NamedSource)o.Source).Name)));
    }

    [Fact]
    public void Offered_PackNeedingANewerModule_IsLeftOut()
    {
        var offered = VoiceSelection.Offered(
            "1.2.0", [], new NamedSource("module"),
            [Pack("pack.new", "1.3.0", Track("cs-CZ", "reader")), Pack("pack.ok", "1.2.0", Track("de-DE", "reader"))]);

        Assert.Equal(["pack.ok"], offered.Select(o => ((NamedSource)o.Source).Name));
    }

    [Fact]
    public void Offered_TrackIdentityAlreadyOffered_KeepsTheFirst()
    {
        var offered = VoiceSelection.Offered(
            "1.0.0", [Track("en-US", "male")], new NamedSource("module"),
            [Pack("pack.en", "1.0.0", Track("en-US", "male"), Track("en-US", "female"))]);

        Assert.Equal(
            [("male", "module"), ("female", "pack.en")],
            offered.Select(o => (o.Track.Id, ((NamedSource)o.Source).Name)));
    }
}
