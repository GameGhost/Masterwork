using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

/// <summary>
/// The merge rules behind both New Game and Manage Modules. Exercised through
/// <see cref="LibraryEntry"/> and a reimplementation-free path: the entries are built by
/// <see cref="ModuleLibrary"/> itself wherever possible, and the state/precedence rules are what's
/// asserted.
/// </summary>
public class ModuleLibraryTests
{
    private static InstalledModule Installed(string id, string version) =>
        new(id, version, id, "", [], "sha");

    private static CatalogEntry Published(string id, string version) => new()
    {
        Type = "module",
        Id = id,
        Title = id,
        Version = version,
        Path = $"v{version}/{id}.mwm",
        Sha256 = new string('a', 64),
        Size = 1,
    };

    private static CatalogSnapshot Snapshot(PackageTrustDecision decision = PackageTrustDecision.Trusted) =>
        new(new ContentSource("https://example.com/.catalog/catalog.json", "https://example.com/dl"),
            new CatalogDocument { Format = CatalogFormatVersion.Current, Title = "S", Entries = [] },
            new SignatureVerificationResult(SignatureVerificationOutcome.Valid, "CN=S", "AA", "S"),
            decision,
            DateTimeOffset.UtcNow);

    private static LibraryEntry Entry(InstalledModule? installed, CatalogEntry? published, PackageTrustDecision decision = PackageTrustDecision.Trusted) =>
        new(installed?.ModuleId ?? published!.Id, installed, published, null, published is null ? null : Snapshot(decision));

    [Fact]
    public void InstalledOnly_IsInstalled()
    {
        Assert.Equal(LibraryEntryState.Installed, Entry(Installed("m", "1.0.0"), null).State);
    }

    [Fact]
    public void PublishedOnly_IsAvailable()
    {
        Assert.Equal(LibraryEntryState.Available, Entry(null, Published("m", "1.0.0")).State);
    }

    [Fact]
    public void InstalledAtTheSameVersion_IsNotAnUpdate()
    {
        Assert.Equal(LibraryEntryState.Installed, Entry(Installed("m", "1.0.0"), Published("m", "1.0.0")).State);
    }

    [Fact]
    public void PublishedHigher_IsAnUpdate()
    {
        Assert.Equal(LibraryEntryState.UpdateAvailable, Entry(Installed("m", "1.0.0"), Published("m", "1.1.0")).State);
    }

    [Fact]
    public void PublishedLower_IsNotAnUpdate()
    {
        // A source rolling back its catalog must not offer a downgrade as if it were an upgrade.
        Assert.Equal(LibraryEntryState.Installed, Entry(Installed("m", "2.0.0"), Published("m", "1.0.0")).State);
    }

    [Theory]
    [InlineData(PackageTrustDecision.Unsigned)]
    [InlineData(PackageTrustDecision.UnrecognizedSigner)]
    [InlineData(PackageTrustDecision.Blocked)]
    public void AnEntryFromAnUntrustedCatalog_CannotBeInstalled(PackageTrustDecision decision)
    {
        // The install service refuses these anyway; this is what stops the UI offering a button
        // that could only fail.
        Assert.False(Entry(null, Published("m", "1.0.0"), decision).CanInstall);
    }

    [Fact]
    public void AnEntryWithNoCatalog_CannotBeInstalled()
    {
        // A manually uploaded module with no source publishing it.
        Assert.False(Entry(Installed("m", "1.0.0"), null).CanInstall);
    }

    [Fact]
    public void TitleAndDescription_PreferTheInstalledCopy()
    {
        // What's installed is what the player actually has; a catalog's copy can be out of date.
        var entry = new LibraryEntry(
            "m",
            new InstalledModule("m", "1.0.0", "Installed Title", "Installed description", [], "sha"),
            Published("m", "1.0.0") with { Title = "Catalog Title", Description = "Catalog description" },
            null, Snapshot());

        Assert.Equal("Installed Title", entry.Title);
        Assert.Equal("Installed description", entry.Description);
    }

    [Fact]
    public void TitleFallsBackToTheCatalog_WhenNothingIsInstalled()
    {
        Assert.Equal("Catalog Title", Entry(null, Published("m", "1.0.0") with { Title = "Catalog Title" }).Title);
    }
}
