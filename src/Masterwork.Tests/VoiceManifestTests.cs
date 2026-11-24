using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class VoiceManifestTests
{
    [Fact]
    public void ModuleManifest_VoicesBlock_ParsesTracksPerLanguageInOrder()
    {
        var manifest = new ManifestParser().Parse("""
            id: 'test.module'
            title: 'Test'
            version: '1.0.0'
            voices:
              en-US:
              - id: 'male'
                name:
                - en-US: 'Male narrator'
                - fr-CA: 'Narrateur'
              - id: 'female'
                name: 'Female narrator'
              cs-CZ:
              - id: 'reader'
                name: 'Reader'
            """);

        Assert.Equal(
            [("en-US", "male"), ("en-US", "female"), ("cs-CZ", "reader")],
            manifest.Voices.Select(v => (v.Language, v.Id)));
        Assert.Equal("Narrateur", manifest.Voices[0].DisplayName("fr-CA"));
        Assert.Equal("Male narrator", manifest.Voices[0].DisplayName("de-DE"));
        Assert.Equal("Female narrator", manifest.Voices[1].DisplayName("fr-CA"));
    }

    [Fact]
    public void ModuleManifest_NoVoicesBlock_HasNoTracks()
    {
        var manifest = new ManifestParser().Parse("""
            id: 'test.module'
            title: 'Test'
            version: '1.0.0'
            """);

        Assert.Empty(manifest.Voices);
    }

    [Fact]
    public void ModuleManifest_TrackWithoutName_IsNamedByItsId()
    {
        var manifest = new ManifestParser().Parse("""
            id: 'test.module'
            title: 'Test'
            version: '1.0.0'
            voices:
              en-US:
              - id: 'male'
            """);

        Assert.Equal("male", Assert.Single(manifest.Voices).DisplayName("en-US"));
    }

    [Fact]
    public void ModuleManifest_DuplicateTrackInOneLanguage_KeepsTheFirstAndWarns()
    {
        var warnings = new ModuleWarnings();
        var manifest = new ManifestParser().Parse("""
            id: 'test.module'
            title: 'Test'
            version: '1.0.0'
            voices:
              en-US:
              - id: 'male'
                name: 'First'
              - id: 'male'
                name: 'Second'
              fr-CA:
              - id: 'male'
                name: 'Other language'
            """, warnings);

        Assert.Equal(["First", "Other language"], manifest.Voices.Select(v => v.DisplayName("en-US")));
        Assert.Contains(warnings.Items, w => w.Kind == "duplicate_voice_track");
    }

    [Fact]
    public void ModuleManifest_TrackWithoutId_IsSkippedWithAWarning()
    {
        var warnings = new ModuleWarnings();
        var manifest = new ManifestParser().Parse("""
            id: 'test.module'
            title: 'Test'
            version: '1.0.0'
            voices:
              en-US:
              - name: 'Nameless'
            """, warnings);

        Assert.Empty(manifest.Voices);
        Assert.NotEmpty(warnings.Items);
    }

    [Fact]
    public void ModuleManifest_VoicesIsKnownField_NoUnmatchedWarning()
    {
        var warnings = new ModuleWarnings();
        new ManifestParser().Parse("""
            id: 'test.module'
            title: 'Test'
            version: '1.0.0'
            voices:
              en-US:
              - id: 'male'
                name: 'Male'
            """, warnings);

        Assert.Empty(warnings.Items);
    }

    [Fact]
    public void VoicePackManifest_ParsesTargetModuleAndTracks()
    {
        var manifest = new VoicePackManifestParser().Parse("""
            type: 'voices'
            id: 'renegade.cost_of_disease.voices.en-US'
            title: 'The Cost of Disease: English narration'
            version: '1.0.0'
            for:
              id: 'renegade.cost_of_disease'
              min_version: '0.7.0'
            voices:
              en-US:
              - id: 'male'
                name: 'Male narrator'
            """);

        Assert.Equal("renegade.cost_of_disease.voices.en-US", manifest.Id);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("renegade.cost_of_disease", manifest.For.ModuleId);
        Assert.Equal("0.7.0", manifest.For.MinVersion);
        var track = Assert.Single(manifest.Voices);
        Assert.Equal(("en-US", "male"), (track.Language, track.Id));
    }

    [Fact]
    public void VoicePackManifest_MissingFor_Throws()
    {
        Assert.Throws<MwsParseException>(() => new VoicePackManifestParser().Parse("""
            type: 'voices'
            id: 'pack'
            title: 'Pack'
            version: '1.0.0'
            voices: {}
            """));
    }

    [Fact]
    public void VoicePackManifest_KnownFields_NoWarnings()
    {
        var warnings = new ModuleWarnings();
        new VoicePackManifestParser().Parse("""
            type: 'voices'
            format: 'mws/0.5'
            id: 'pack'
            title: 'Pack'
            description: 'English narration'
            version: '1.0.0'
            for:
              id: 'module'
              min_version: '1.0.0'
            voices:
              en-US:
              - id: 'male'
            """, warnings);

        Assert.Empty(warnings.Items);
    }
}
