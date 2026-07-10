using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class LoadedModuleContentTests
{
    private static string MakeSourceDirectory(bool includeStyle)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mw-content-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path.Combine(dir, "assets"));

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
            layout: 'hub'
            nodes: []
            """);

        if (includeStyle)
        {
            File.WriteAllText(Path.Combine(dir, "assets", "style.css"), ".layout-hub { color: red; }");
        }

        return dir;
    }

    [Fact]
    public void FromPackage_DecodesStyleCssFromDefaultPath()
    {
        var dir = MakeSourceDirectory(includeStyle: true);
        try
        {
            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);
            var module = new ModuleLoader().LoadFromSources(contents.PassageYamls, contents.VariablesYaml);

            var loaded = LoadedModuleContent.FromPackage(contents, module);

            Assert.Equal(".layout-hub { color: red; }", loaded.StyleCss);
            Assert.True(loaded.Assets.ContainsKey("assets/style.css"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FromPackage_NoStyleFile_StyleCssIsNull()
    {
        var dir = MakeSourceDirectory(includeStyle: false);
        try
        {
            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);
            var module = new ModuleLoader().LoadFromSources(contents.PassageYamls, contents.VariablesYaml);

            var loaded = LoadedModuleContent.FromPackage(contents, module);

            Assert.Null(loaded.StyleCss);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
