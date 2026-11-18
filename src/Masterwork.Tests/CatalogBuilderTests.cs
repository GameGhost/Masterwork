using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class CatalogBuilderTests
{
    private static readonly byte[] FakeImage = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4];

    private static byte[] Package(string manifestYaml, params (string Path, byte[] Bytes)[] extraEntries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("manifest.yaml");
            using (var stream = entry.Open())
            {
                stream.Write(Encoding.UTF8.GetBytes(manifestYaml));
            }

            foreach (var (path, bytes) in extraEntries)
            {
                using var extraStream = archive.CreateEntry(path).Open();
                extraStream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    private const string ModuleManifestYaml = """
        type: 'module'
        id: 'test.module'
        title: 'The Test Module'
        version: '1.2.3'
        description: 'A module for testing.'
        languages:
          - 'en-US'
          - 'fr-CA'
        info:
          players-min: 2
          players-max: 4
          playtime: '180 minutes'
        dependencies:
          - id: 'test.assets'
            version: '0.1.0'
        """;

    private const string AssetPackManifestYaml = """
        type: 'assets'
        id: 'test.assets'
        title: 'Test Assets'
        version: '0.1.0'
        description: 'Shared assets.'
        """;

    [Fact]
    public void Build_CopiesBrowseMetadataOutOfTheRealManifest()
    {
        var bytes = Package(ModuleManifestYaml);

        var built = CatalogBuilder.Build("Test Source", [new CatalogBuilder.PackageInput("v1.2.3/test.mwm", bytes)]);

        var entry = Assert.Single(built.Catalog.Entries);
        Assert.Equal("module", entry.Type);
        Assert.Equal("test.module", entry.Id);
        Assert.Equal("The Test Module", entry.Title);
        Assert.Equal("1.2.3", entry.Version);
        Assert.Equal("A module for testing.", entry.Description);
        Assert.Equal(["en-US", "fr-CA"], entry.Languages);
        Assert.Equal(new CatalogPlayers(2, 4), entry.Players);
        Assert.Equal("180 minutes", entry.Playtime);
        Assert.Equal("test.assets", Assert.Single(entry.Dependencies).Id);
    }

    [Fact]
    public void Build_MeasuresHashAndSizeFromTheActualBytes()
    {
        var bytes = Package(ModuleManifestYaml);
        var expected = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var built = CatalogBuilder.Build("Test Source", [new CatalogBuilder.PackageInput("v1.2.3/test.mwm", bytes)]);

        var entry = Assert.Single(built.Catalog.Entries);
        Assert.Equal(expected, entry.Sha256);
        Assert.Equal(bytes.LongLength, entry.Size);
    }

    [Fact]
    public void Build_TagsAnAssetPackByItsManifestTypeAndGivesItNoPlayMetadata()
    {
        var built = CatalogBuilder.Build(
            "Test Source",
            [new CatalogBuilder.PackageInput("v0.1.0/test.mwassets", Package(AssetPackManifestYaml))]);

        var entry = Assert.Single(built.Catalog.Entries);
        Assert.Equal("assets", entry.Type);
        Assert.Equal("Test Assets", entry.Title);

        // An asset pack is a leaf content type — no players, no playtime, no dependencies of its own.
        Assert.Null(entry.Players);
        Assert.Null(entry.Playtime);
        Assert.Empty(entry.Dependencies);
    }

    [Fact]
    public void Build_ProducesACatalogThatParsesBack()
    {
        var built = CatalogBuilder.Build("Test Source",
        [
            new CatalogBuilder.PackageInput("v1.2.3/test.mwm", Package(ModuleManifestYaml)),
            new CatalogBuilder.PackageInput("v0.1.0/test.mwassets", Package(AssetPackManifestYaml)),
        ]);

        var reparsed = CatalogParser.Parse(CatalogParser.Write(built.Catalog));

        Assert.Equal(CatalogFormatVersion.Current, reparsed.Format);
        Assert.Equal(2, reparsed.Entries.Count);
    }

    [Fact]
    public void Build_KeepsEntriesInTheOrderGiven()
    {
        var built = CatalogBuilder.Build("Test Source",
        [
            new CatalogBuilder.PackageInput("v0.1.0/test.mwassets", Package(AssetPackManifestYaml)),
            new CatalogBuilder.PackageInput("v1.2.3/test.mwm", Package(ModuleManifestYaml)),
        ]);

        Assert.Equal(["test.assets", "test.module"], built.Catalog.Entries.Select(e => e.Id));
    }

    [Fact]
    public void Build_ExtractsTheThumbnailNamedByTheManifest()
    {
        var bytes = Package(
            ModuleManifestYaml + "\nthumbnail:\n  image: 'image://tile_art'\n",
            ("assets/images/tile_art.png", FakeImage));

        var built = CatalogBuilder.Build("Test Source", [new CatalogBuilder.PackageInput("v1.2.3/test.mwm", bytes)]);

        // The entry points at a path relative to the catalog's own directory, and the bytes come out
        // alongside so they can be published there.
        Assert.Equal("thumbnails/test.module.png", Assert.Single(built.Catalog.Entries).Thumbnail!.Path);
        var thumbnail = Assert.Single(built.Thumbnails);
        Assert.Equal("thumbnails/test.module.png", thumbnail.RelativePath);
        Assert.Equal(FakeImage, thumbnail.Bytes);

        // The hash is what lets a client skip re-downloading art that didn't change between refreshes.
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(FakeImage)).ToLowerInvariant(),
            built.Catalog.Entries[0].Thumbnail!.Hash);
    }

    [Fact]
    public void Build_LeavesThumbnailHashUnsetWhenThereIsNoThumbnail()
    {
        var built = CatalogBuilder.Build(
            "Test Source",
            [new CatalogBuilder.PackageInput("v1.2.3/test.mwm", Package(ModuleManifestYaml))]);

        Assert.Null(Assert.Single(built.Catalog.Entries).Thumbnail);
    }

    [Fact]
    public void Build_ProbesExtensionsTheSameWayTheAppDoes()
    {
        var bytes = Package(
            ModuleManifestYaml + "\nthumbnail:\n  image: 'image://tile_art'\n",
            ("assets/images/tile_art.webp", FakeImage));

        var built = CatalogBuilder.Build("Test Source", [new CatalogBuilder.PackageInput("v1.2.3/test.mwm", bytes)]);

        Assert.Equal("thumbnails/test.module.webp", Assert.Single(built.Catalog.Entries).Thumbnail!.Path);
    }

    [Fact]
    public void Build_GivesAnAssetPackNoThumbnail()
    {
        // Asset packs are never browsed or installed directly — only pulled in as a dependency — so
        // even one carrying tile art would have nowhere to show it.
        var bytes = Package(
            AssetPackManifestYaml + "\nthumbnail:\n  image: 'image://tile_art'\n",
            ("assets/images/tile_art.png", FakeImage));

        var built = CatalogBuilder.Build("Test Source", [new CatalogBuilder.PackageInput("v0.1.0/test.mwassets", bytes)]);

        Assert.Null(Assert.Single(built.Catalog.Entries).Thumbnail);
        Assert.Empty(built.Thumbnails);
    }

    [Theory]
    [InlineData("")]                                            // no thumbnail declared at all
    [InlineData("\nthumbnail:\n  image: 'image://missing'\n")]  // declared, but not in the package
    public void Build_SurvivesAModuleWithNoUsableThumbnail(string thumbnailBlock)
    {
        var built = CatalogBuilder.Build(
            "Test Source",
            [new CatalogBuilder.PackageInput("v1.2.3/test.mwm", Package(ModuleManifestYaml + thumbnailBlock))]);

        Assert.Null(Assert.Single(built.Catalog.Entries).Thumbnail);
        Assert.Empty(built.Thumbnails);
    }

    [Fact]
    public void Build_StampsUpdatedAsIso8601Utc()
    {
        var built = CatalogBuilder.Build(
            "Test Source",
            [new CatalogBuilder.PackageInput("v1.2.3/test.mwm", Package(ModuleManifestYaml))],
            updated: new DateTimeOffset(2026, 9, 15, 8, 30, 0, TimeSpan.FromHours(-4)));

        var json = Encoding.UTF8.GetString(CatalogParser.Write(built.Catalog));

        // Normalized to UTC with whole seconds, not the writer's own offset or tick precision.
        Assert.Contains("\"updated\": \"2026-09-15T12:30:00Z\"", json);
    }

    [Fact]
    public void Build_RejectsAPathThatWouldEscapeTheContentBase()
    {
        var bytes = Package(ModuleManifestYaml);

        Assert.Throws<ArgumentException>(() =>
            CatalogBuilder.Build("Test Source", [new CatalogBuilder.PackageInput("https://evil.example.com/x.mwm", bytes)]));
    }

    [Fact]
    public void Build_RejectsAPackageWithNoManifest()
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("assets/style.css");
        }

        Assert.Throws<ArgumentException>(() =>
            CatalogBuilder.Build("Test Source", [new CatalogBuilder.PackageInput("v1/x.mwm", buffer.ToArray())]));
    }
}
