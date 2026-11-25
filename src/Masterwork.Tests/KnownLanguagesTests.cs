using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class KnownLanguagesTests
{
    private static InstalledModule Module(string[] text, string[] voice) =>
        new("m", "1.0.0", "M", "", text, "sha", VoiceLanguages: voice);

    private static CatalogEntry Entry(string type, params string[] languages) =>
        new() { Type = type, Id = type, Title = type, Version = "1.0.0", Path = "p", Sha256 = "s", Languages = languages };

    private static InstalledVoicePack Pack(string language) =>
        new(new VoicePackManifest
        {
            Id = "p", Title = "P", Version = "1.0.0",
            For = new VoicePackTarget { ModuleId = "m", MinVersion = "1.0.0" },
            Voices = [new VoiceTrack { Language = language, Id = "reader" }],
        }, EmptyModuleAssetSource.Instance);

    [Fact]
    public void TextLanguages_ComeFromInstalledAndCatalogModules_NotOtherPackages()
    {
        var known = KnownLanguages.From(
            [Module(["en-US"], [])],
            [],
            [Entry("module", "fr-CA"), Entry("assets", "de-DE"), Entry("voices", "cs-CZ")]);

        Assert.Equal(["en-US", "fr-CA"], known.Text);
    }

    [Fact]
    public void VoiceLanguages_ComeFromModuleTracksInstalledPacksAndCatalogPacks()
    {
        var known = KnownLanguages.From(
            [Module(["en-US", "fr-CA"], ["en-US"])],
            [Pack("es-MX")],
            [Entry("module", "de-DE"), Entry("voices", "fr-CA")]);

        Assert.Equal(["en-US", "es-MX", "fr-CA"], known.Voice);
    }

    [Fact]
    public void Languages_AreDeduplicatedIgnoringCaseAndSorted()
    {
        var known = KnownLanguages.From(
            [Module(["fr-CA", "en-US"], ["en-US"]), Module(["FR-ca"], ["EN-us"])],
            [],
            [Entry("module", "en-US")]);

        Assert.Equal(["en-US", "fr-CA"], known.Text);
        Assert.Equal(["en-US"], known.Voice);
    }
}
