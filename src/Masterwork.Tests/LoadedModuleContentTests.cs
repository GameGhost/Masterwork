using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class LoadedModuleContentTests
{
    // Minimal in-memory IModuleAssetSource wrapping ModulePackageContents.Assets (still a plain
    // Dictionary<string, byte[]> — ModulePackage.ReadFromBytes is unchanged, still used by the
    // packer tool and this kind of whole-package test) — just enough to drive BuildAsync without
    // needing a real IndexedDB/filesystem-backed implementation.
    private sealed class DictionaryModuleAssetSource(IReadOnlyDictionary<string, byte[]> assets) : IModuleAssetSource
    {
        public Task<byte[]?> GetAssetAsync(string assetPath) =>
            Task.FromResult(assets.TryGetValue(assetPath, out var bytes) ? bytes : null);

        public Task<string?> GetAssetUrlAsync(string assetPath, string mimeType) =>
            Task.FromResult(assets.TryGetValue(assetPath, out var bytes)
                ? $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}"
                : null);

        public Task<IReadOnlyList<string>> ListAssetPathsAsync() =>
            Task.FromResult<IReadOnlyList<string>>([.. assets.Keys]);
    }

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
    public async Task BuildAsync_DecodesStyleCssFromDefaultPath()
    {
        var dir = MakeSourceDirectory(includeStyle: true);
        try
        {
            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);
            var module = new ModuleLoader().LoadFromSources(contents.PassageYamls, contents.VariablesYaml);
            var assets = new DictionaryModuleAssetSource(contents.Assets);

            var loaded = await LoadedModuleContent.BuildAsync(module, contents.ManifestYaml, assets);

            Assert.Equal(".layout-hub { color: red; }", loaded.StyleCss);
            Assert.NotNull(await loaded.Assets.GetAssetAsync("assets/style.css"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_NoStyleFile_StyleCssIsNull()
    {
        var dir = MakeSourceDirectory(includeStyle: false);
        try
        {
            var bytes = ModulePackage.WriteToBytes(dir);
            var contents = ModulePackage.ReadFromBytes(bytes);
            var module = new ModuleLoader().LoadFromSources(contents.PassageYamls, contents.VariablesYaml);
            var assets = new DictionaryModuleAssetSource(contents.Assets);

            var loaded = await LoadedModuleContent.BuildAsync(module, contents.ManifestYaml, assets);

            Assert.Null(loaded.StyleCss);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
