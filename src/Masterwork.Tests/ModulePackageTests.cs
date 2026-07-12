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
        File.WriteAllText(Path.Combine(dir, "es.restext"), "Greeting=Hola");
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
            Assert.Equal("Greeting=Hello", contents.RestextByLocale["en-US"]);
            Assert.Equal("Greeting=Hola", contents.RestextByLocale["es"]);
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
    public void ReadFromBytes_PassagesAndOverrideSubfolders_SeparatedCorrectly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-package-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(dir, "passages"));
        Directory.CreateDirectory(Path.Combine(dir, "passages-override"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "manifest.yaml"), """
                id: 'test.module'
                title: 'Test Module'
                version: '1.0.0'
                """);
            File.WriteAllText(Path.Combine(dir, "passages", "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes: []
                """);
            File.WriteAllText(Path.Combine(dir, "passages-override", "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub'
                nodes:
                - type: 'text'
                  value: 'Hand-authored'
                """);

            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);

            Assert.Single(contents.PassageYamls);
            Assert.Single(contents.OverridePassageYamls);
            Assert.Contains("nodes: []", contents.PassageYamls[0]);
            Assert.Contains("Hand-authored", contents.OverridePassageYamls[0]);

            var module = new ModuleLoader().LoadFromSources(
                contents.PassageYamls, contents.VariablesYaml, overridePassageYamls: contents.OverridePassageYamls);
            var text = Assert.IsType<TextNode>(module.Passages["Start"].Nodes.Single());
            Assert.Equal("Hand-authored", text.Value);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReadFromBytes_LayoutsFolder_RoundTripsAndLoads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-package-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(dir, "layouts"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "manifest.yaml"), """
                id: 'test.module'
                title: 'Test Module'
                version: '1.0.0'
                """);
            File.WriteAllText(Path.Combine(dir, "001-Start.mws.yaml"), """
                format: 'mws/0.3'
                passage_id: 'Start'
                tags:
                - 'Begins-Here'
                layout: 'hub_early'
                nodes: []
                """);
            File.WriteAllText(Path.Combine(dir, "layouts", "hub_early.mws.yaml"), """
                format: 'mws/0.4'
                layout_id: 'hub_early'
                header:
                - type: 'text'
                  value: 'Chrome text'
                """);

            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);

            Assert.Single(contents.LayoutYamls);
            Assert.Contains("layout_id: 'hub_early'", contents.LayoutYamls[0]);

            var module = new ModuleLoader().LoadFromSources(
                contents.PassageYamls, layoutChromeYamls: contents.LayoutYamls);
            var text = Assert.IsType<TextNode>(module.LayoutChrome["hub_early"].Header.Single());
            Assert.Equal("Chrome text", text.Value);
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
            var restext = ModuleLocales.SelectRestext(contents.RestextByLocale, preferredLocale: null);

            var module = new ModuleLoader().LoadFromSources(contents.PassageYamls, contents.VariablesYaml, restext);

            Assert.Equal("Start", module.StartPassageId);
            Assert.True(module.Variables.ContainsKey("foo"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
