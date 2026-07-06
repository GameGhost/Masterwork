using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class ModulePackageTests
{
    private static string MakeSourceDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-package-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "assets", "images"));

        File.WriteAllText(Path.Combine(dir, "manifest.yaml"), """
            id: 'test.module'
            title: 'Test Module'
            version: '1.0.0'
            """);
        File.WriteAllText(Path.Combine(dir, "_variables.yaml"), """
            standard_variables: []
            variables:
              foo:
                type: 'int'
                default: 0
            """);
        File.WriteAllText(Path.Combine(dir, "en-US.restext"), "Greeting=Hello");
        File.WriteAllText(Path.Combine(dir, "001-Start.mws.yaml"), """
            format: 'mws/0.3'
            passage_id: 'Start'
            tags:
            - 'Begins-Here'
            layout: 'hub'
            nodes: []
            """);
        File.WriteAllBytes(Path.Combine(dir, "assets", "images", "icon.png"), [1, 2, 3, 4]);

        return dir;
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllContent()
    {
        var dir = MakeSourceDirectory();
        try
        {
            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);

            Assert.Contains("id: 'test.module'", contents.ManifestYaml);
            Assert.Contains("foo:", contents.VariablesYaml);
            Assert.Equal("Greeting=Hello", contents.RestextText);
            Assert.Single(contents.PassageYamls);
            Assert.Contains("passage_id: 'Start'", contents.PassageYamls[0]);
            Assert.True(contents.Assets.ContainsKey("assets/images/icon.png"));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, contents.Assets["assets/images/icon.png"]);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadContents_LoadIntoModuleLoader_ProducesPlayableModule()
    {
        var dir = MakeSourceDirectory();
        try
        {
            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);

            var module = new ModuleLoader().LoadFromSources(contents.PassageYamls, contents.VariablesYaml, contents.RestextText);

            Assert.Equal("Start", module.StartPassageId);
            Assert.True(module.Variables.ContainsKey("foo"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
