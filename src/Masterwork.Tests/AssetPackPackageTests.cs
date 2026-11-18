using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class AssetPackPackageTests
{
    private static string MakeSourceDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-assetpack-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "assets", "icons"));
        Directory.CreateDirectory(Path.Combine(dir, "layouts"));

        File.WriteAllText(Path.Combine(dir, "manifest.yaml"), """
            id: 'MFW_Common_Assets'
            title: 'MFW Common Assets'
            version: '1.0.0'
            """);
        File.WriteAllText(Path.Combine(dir, "_variables.yaml"), """
            variables:
              sharedCounter:
                type: 'int'
                default: 0
            """);
        File.WriteAllText(Path.Combine(dir, "en-US.restext"), "Shared_001=Common text");
        File.WriteAllText(Path.Combine(dir, "es.restext"), "Shared_001=Texto comun");
        File.WriteAllText(Path.Combine(dir, "layouts", "hub_shared.mws.yaml"), """
            format: 'mws/0.4'
            layout_id: 'hub_shared'
            header:
            - type: 'text'
              value: 'restext://Shared_001'
            """);
        File.WriteAllBytes(Path.Combine(dir, "assets", "icons", "star.png"), [9, 8, 7, 6]);

        return dir;
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllContent()
    {
        var dir = MakeSourceDirectory();
        try
        {
            var bytes = AssetPackPackage.WriteToBytes(dir);
            var contents = AssetPackPackage.ReadFromBytes(bytes);

            Assert.Contains("id: 'MFW_Common_Assets'", contents.ManifestYaml);
            Assert.Contains("sharedCounter:", contents.VariablesYaml);
            Assert.Equal("Shared_001=Common text", contents.RestextByLocale["en-US"]);
            Assert.Equal("Shared_001=Texto comun", contents.RestextByLocale["es"]);
            Assert.Single(contents.LayoutYamls);
            Assert.Contains("layout_id: 'hub_shared'", contents.LayoutYamls[0]);
            Assert.True(contents.Assets.ContainsKey("assets/icons/star.png"));
            Assert.Equal(new byte[] { 9, 8, 7, 6 }, contents.Assets["assets/icons/star.png"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadManifestOnly_DoesNotRequireOtherEntries()
    {
        var dir = MakeSourceDirectory();
        try
        {
            var bytes = AssetPackPackage.WriteToBytes(dir);
            var manifestYaml = AssetPackPackage.ReadManifestOnly(bytes);

            Assert.NotNull(manifestYaml);
            Assert.Contains("id: 'MFW_Common_Assets'", manifestYaml);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadFromBytes_NoVariablesOrLayoutsOrAssets_ContentsAreEmptyNotNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-assetpack-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "manifest.yaml"), """
                id: 'test.pack'
                title: 'Test Pack'
                version: '1.0.0'
                """);

            var bytes = AssetPackPackage.WriteToBytes(dir);
            var contents = AssetPackPackage.ReadFromBytes(bytes);

            Assert.Null(contents.VariablesYaml);
            Assert.Empty(contents.RestextByLocale);
            Assert.Empty(contents.LayoutYamls);
            Assert.Empty(contents.Assets);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractEntriesAsync_ReportsProgressAndStreamsEachEntry()
    {
        var dir = MakeSourceDirectory();
        try
        {
            var bytes = AssetPackPackage.WriteToBytes(dir);
            var seen = new List<AssetPackPackageEntryKind>();
            var lastProgress = (Done: 0, Total: 0);

            await AssetPackPackage.ExtractEntriesAsync(bytes, entry =>
            {
                seen.Add(entry.Kind);
                return ValueTask.CompletedTask;
            }, new Progress<(int Done, int Total)>(p => lastProgress = p));

            Assert.Contains(AssetPackPackageEntryKind.Manifest, seen);
            Assert.Contains(AssetPackPackageEntryKind.Variables, seen);
            Assert.Contains(AssetPackPackageEntryKind.Restext, seen);
            Assert.Contains(AssetPackPackageEntryKind.Layout, seen);
            Assert.Contains(AssetPackPackageEntryKind.Asset, seen);
            Assert.Equal(lastProgress.Total, lastProgress.Done);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
