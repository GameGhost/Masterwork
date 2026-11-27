using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class VoicePackOffersTests
{
    private const string ValidSha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly ContentSource Source = new("https://example.com/catalog.json", "https://example.com/dl");

    private static CatalogEntry VoiceEntry(string id, string language, string version = "1.0.0", string minVersion = "1.0.0", string moduleId = "test.module") => new()
    {
        Type = "voices",
        Id = id,
        Title = id,
        Version = version,
        Path = $"v{version}/{id}.mwassets",
        Sha256 = ValidSha,
        Languages = [language],
        For = new VoicePackTarget { ModuleId = moduleId, MinVersion = minVersion },
    };

    private static CatalogEntry ModuleEntry(string version) => new()
    {
        Type = "module", Id = "test.module", Title = "Test Module", Version = version, Path = $"v{version}/test.mwm", Sha256 = ValidSha,
    };

    private static CatalogSnapshot Snapshot(PackageTrustDecision decision, params CatalogEntry[] entries) =>
        new(Source,
            new CatalogDocument { Format = CatalogFormatVersion.Current, Title = "Test", Entries = entries },
            new SignatureVerificationResult(SignatureVerificationOutcome.Valid, "CN=Test", "AA", "Test"),
            decision,
            DateTimeOffset.UtcNow);

    private static InstalledModule Module(string version = "1.0.0", string[]? builtInVoices = null) =>
        new("test.module", version, "Test Module", "", ["en-US", "fr-CA"], "hash", VoiceLanguages: builtInVoices ?? []);

    private static VoicePackManifest Installed(string id, string version, string language) => new()
    {
        Id = id,
        Title = id,
        Version = version,
        For = new VoicePackTarget { ModuleId = "test.module", MinVersion = "1.0.0" },
        Voices = [new VoiceTrack { Language = language, Id = "reader" }],
    };

    // ── What is offered ─────────────────────────────────────────────────────

    [Fact]
    public void APackForAnInstalledModule_IsOffered()
    {
        var offers = VoicePackOffers.From([Snapshot(PackageTrustDecision.Trusted, VoiceEntry("voices.en", "en-US"))], [Module()], []);

        var offer = Assert.Single(offers);
        Assert.Equal(VoicePackOfferState.Available, offer.State);
        Assert.Equal("test.module", offer.Module.ModuleId);
    }

    [Fact]
    public void PacksForModulesThatArentInstalled_OrFromUntrustedCatalogs_AreNotOffered()
    {
        var offers = VoicePackOffers.From(
            [
                Snapshot(PackageTrustDecision.Trusted, VoiceEntry("voices.other", "en-US", moduleId: "other.module")),
                Snapshot(PackageTrustDecision.Unsigned, VoiceEntry("voices.en", "en-US")),
            ],
            [Module()], []);

        Assert.Empty(offers);
    }

    [Fact]
    public void AnInstalledPack_IsOfferedOnlyAsAnUpdate_NeverADowngrade()
    {
        var snapshot = Snapshot(PackageTrustDecision.Trusted, VoiceEntry("voices.en", "en-US", version: "1.1.0"), VoiceEntry("voices.cs", "cs-CZ", version: "1.0.0"));

        var offers = VoicePackOffers.From([snapshot], [Module()], [Installed("voices.en", "1.0.0", "en-US"), Installed("voices.cs", "2.0.0", "cs-CZ")]);

        var offer = Assert.Single(offers);
        Assert.Equal("voices.en", offer.Entry.Id);
        Assert.Equal(VoicePackOfferState.UpdateAvailable, offer.State);
    }

    [Fact]
    public void APackNeedingANewerModule_OffersThatModuleUpdate_WhenTheCatalogHasIt()
    {
        var snapshot = Snapshot(PackageTrustDecision.Trusted, ModuleEntry("1.3.0"), VoiceEntry("voices.en", "en-US", minVersion: "1.2.0"));

        var offer = Assert.Single(VoicePackOffers.From([snapshot], [Module("1.1.0")], []));

        Assert.Equal(VoicePackOfferState.NeedsModuleUpdate, offer.State);
        Assert.Equal("1.3.0", offer.ModuleUpdate?.Version);
    }

    [Fact]
    public void TheFirstCatalogOfferingAPack_Wins()
    {
        var first = Snapshot(PackageTrustDecision.Trusted, VoiceEntry("voices.en", "en-US", version: "1.0.0"));
        var second = Snapshot(PackageTrustDecision.Trusted, VoiceEntry("voices.en", "en-US", version: "2.0.0"));

        Assert.Equal("1.0.0", Assert.Single(VoicePackOffers.From([first, second], [Module()], [])).Entry.Version);
    }

    // ── Installing with a module ────────────────────────────────────────────

    private static VoicePackOffer? ForInstall(AppSettings settings, InstalledModule module, IReadOnlyList<VoicePackManifest> installed, params CatalogEntry[] entries) =>
        VoicePackOffers.ForModuleInstall(settings, module, VoicePackOffers.From([Snapshot(PackageTrustDecision.Trusted, entries)], [module], installed), installed);

    [Fact]
    public void ModuleInstall_PicksThePackForTheEffectiveVoiceLanguage()
    {
        // Best match follows the text language, which follows the app language here.
        var settings = AppSettings.Default with { UiLocale = "fr-CA" };

        var offer = ForInstall(settings, Module(), [], VoiceEntry("voices.en", "en-US"), VoiceEntry("voices.fr", "fr-CA"));

        Assert.Equal("voices.fr", offer?.Entry.Id);
    }

    [Fact]
    public void ModuleInstall_UnderNone_InstallsNoPack()
    {
        var settings = AppSettings.Default with { VoiceLanguage = VoiceSelection.None };

        Assert.Null(ForInstall(settings, Module(), [], VoiceEntry("voices.en", "en-US")));
    }

    [Fact]
    public void ModuleInstall_AnExplicitLanguageWithNoPack_InstallsNone_RatherThanAnotherLanguage()
    {
        var settings = AppSettings.Default with { VoiceLanguage = "cs-CZ" };

        Assert.Null(ForInstall(settings, Module(), [], VoiceEntry("voices.en", "en-US")));
    }

    [Fact]
    public void ModuleInstall_ALanguageAlreadyNarrated_InstallsNothingMore()
    {
        var settings = AppSettings.Default with { VoiceLanguage = "en-US" };

        Assert.Null(ForInstall(settings, Module(builtInVoices: ["en-US"]), [], VoiceEntry("voices.en", "en-US")));
        Assert.Null(ForInstall(settings, Module(), [Installed("voices.other", "1.0.0", "en-US")], VoiceEntry("voices.en", "en-US")));
    }

    [Fact]
    public void ModuleInstall_SkipsAPackTheModuleIsTooOldFor()
    {
        var settings = AppSettings.Default with { VoiceLanguage = "en-US" };

        Assert.Null(ForInstall(settings, Module("1.0.0"), [], VoiceEntry("voices.en", "en-US", minVersion: "2.0.0")));
    }

    // ── What playing with a voice needs ─────────────────────────────────────

    private static VoicePackNeed Need(InstalledModule module, string? language, IReadOnlyList<VoicePackManifest> installed, params CatalogEntry[] entries) =>
        VoicePackOffers.NeedFor(module, language, VoicePackOffers.From([Snapshot(PackageTrustDecision.Trusted, entries)], [module], installed), installed);

    [Fact]
    public void PlayingWithNoVoice_NeedsNothing()
    {
        Assert.Equal(VoicePackNeedKind.Ready, Need(Module(), null, [], VoiceEntry("voices.en", "en-US")).Kind);
    }

    [Fact]
    public void ALanguageTheModuleItselfNarrates_NeedsNothing()
    {
        Assert.Equal(VoicePackNeedKind.Ready, Need(Module(builtInVoices: ["en-US"]), "en-US", [], VoiceEntry("voices.en", "en-US")).Kind);
    }

    [Fact]
    public void ALanguageOnlyACatalogPackHas_NeedsThatPackDownloaded()
    {
        var need = Need(Module(), "fr-CA", [], VoiceEntry("voices.en", "en-US"), VoiceEntry("voices.fr", "fr-CA"));

        Assert.Equal(VoicePackNeedKind.Missing, need.Kind);
        Assert.Equal("voices.fr", need.Offer?.Entry.Id);
    }

    [Fact]
    public void ALanguageNoInstallablePackHas_IsUnavailable()
    {
        Assert.Equal(VoicePackNeedKind.Unavailable, Need(Module(), "cs-CZ", [], VoiceEntry("voices.en", "en-US")).Kind);

        var tooNew = Need(Module("1.0.0"), "en-US", [], VoiceEntry("voices.en", "en-US", minVersion: "2.0.0"));
        Assert.Equal(VoicePackNeedKind.Unavailable, tooNew.Kind);
        Assert.Equal(VoicePackOfferState.NeedsModuleUpdate, tooNew.Offer?.State);
    }

    [Fact]
    public void ALanguageAnInstalledPackNarrates_OffersThatPacksUpdate_WhenThereIsOne()
    {
        var need = Need(Module(), "en-US", [Installed("voices.en", "1.0.0", "en-US")], VoiceEntry("voices.en", "en-US", version: "1.1.0"));

        Assert.Equal(VoicePackNeedKind.UpdateAvailable, need.Kind);
        Assert.Equal("1.1.0", need.Offer?.Entry.Version);

        Assert.Equal(VoicePackNeedKind.Ready,
            Need(Module(), "en-US", [Installed("voices.en", "1.1.0", "en-US")], VoiceEntry("voices.en", "en-US", version: "1.1.0")).Kind);
    }

    [Fact]
    public void VoiceLanguages_AreThoseNarratedOrInstallable_NotThoseNeedingANewerModule()
    {
        var module = Module(builtInVoices: ["en-US"]);
        IReadOnlyList<VoicePackManifest> installed = [Installed("voices.cs", "1.0.0", "cs-CZ")];
        var offers = VoicePackOffers.From(
            [Snapshot(PackageTrustDecision.Trusted, VoiceEntry("voices.fr", "fr-CA"), VoiceEntry("voices.de", "de-DE", minVersion: "2.0.0"))],
            [module], installed);

        Assert.Equal(["en-US", "cs-CZ", "fr-CA"], VoicePackOffers.VoiceLanguages(module, offers, installed));
    }
}
