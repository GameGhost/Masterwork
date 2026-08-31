using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class ModuleDependencyResolverTests
{
    private sealed class FakeAssetPackStore : IAssetPackStore
    {
        public Dictionary<(string Id, string Version), AssetPackPackageContents> Installed { get; } = [];

        public Task<IReadOnlyList<InstalledAssetPack>> ListAsync() => throw new NotImplementedException();

        public Task<AssetPackPackageContents?> LoadContentAsync(string assetPackId, string version) =>
            Task.FromResult(Installed.TryGetValue((assetPackId, version), out var content) ? content : null);

        public Task<InstalledAssetPack> InstallAsync(byte[] mwassetsBytes, string sha256, AssetPackInstallSource source, IProgress<(int Done, int Total)>? progress = null) =>
            throw new NotImplementedException();

        public Task DeleteAsync(string assetPackId, string version) => throw new NotImplementedException();
    }

    private static AssetPackPackageContents MakeContents(
        string? manifestYaml = null, Dictionary<string, string>? restextByLocale = null,
        IReadOnlyList<string>? layoutYamls = null, string? variablesYaml = null) =>
        new(manifestYaml, variablesYaml, restextByLocale ?? [], layoutYamls ?? [], new Dictionary<string, byte[]>());

    [Fact]
    public async Task ResolveAsync_NoDependencies_ReturnsAllEmpty()
    {
        var store = new FakeAssetPackStore();

        var result = await ModuleDependencyResolver.ResolveAsync([], store, "en-US");

        Assert.Empty(result.DependencyRestexts);
        Assert.Empty(result.LayoutChromeYamls);
        Assert.Empty(result.AdditionalVariableYamls);
        Assert.Empty(result.MissingDependencyWarnings);
    }

    [Fact]
    public async Task ResolveAsync_DependencyNotInstalled_ReturnsMissingWarningNoFailure()
    {
        var store = new FakeAssetPackStore();
        var dependencies = new[] { new ModuleDependency { Id = "MFW_Common_Assets", Version = "1.0.0" } };

        var result = await ModuleDependencyResolver.ResolveAsync(dependencies, store, "en-US");

        Assert.Empty(result.DependencyRestexts);
        var warning = Assert.Single(result.MissingDependencyWarnings);
        Assert.Contains("MFW_Common_Assets", warning);
        Assert.Contains("1.0.0", warning);
    }

    [Fact]
    public async Task ResolveAsync_DependencyHasNoDeclaredVersion_ReturnsMissingWarningWithoutLookup()
    {
        var store = new FakeAssetPackStore();
        var dependencies = new[] { new ModuleDependency { Id = "MFW_Common_Assets", Version = null } };

        var result = await ModuleDependencyResolver.ResolveAsync(dependencies, store, "en-US");

        var warning = Assert.Single(result.MissingDependencyWarnings);
        Assert.Contains("no declared version", warning);
    }

    [Fact]
    public async Task ResolveAsync_DependencyInstalled_BuildsDependencyRestextForModuleResolvedLocale()
    {
        var store = new FakeAssetPackStore();
        store.Installed[("MFW_Common_Assets", "1.0.0")] = MakeContents(
            manifestYaml: "id: 'MFW_Common_Assets'\ntitle: 'x'\nversion: '1.0.0'\ndefault_locale: 'en-US'\n",
            restextByLocale: new() { ["es"] = "Shared_001=Texto compartido\n", ["en-US"] = "Shared_001=Shared text\n" });
        var dependencies = new[] { new ModuleDependency { Id = "MFW_Common_Assets", Version = "1.0.0" } };

        var result = await ModuleDependencyResolver.ResolveAsync(dependencies, store, "es");

        var dep = Assert.Single(result.DependencyRestexts);
        Assert.Equal("Shared_001=Texto compartido\n", dep.RestextText);
        Assert.Equal("Shared_001=Shared text\n", dep.DefaultRestextText);
        Assert.Empty(result.MissingDependencyWarnings);
    }

    [Fact]
    public async Task ResolveAsync_ModuleResolvedLocaleNotInAssetPack_DefaultRestextStillUsed()
    {
        var store = new FakeAssetPackStore();
        store.Installed[("MFW_Common_Assets", "1.0.0")] = MakeContents(
            manifestYaml: "id: 'MFW_Common_Assets'\ntitle: 'x'\nversion: '1.0.0'\n", // default_locale omitted -> en-US
            restextByLocale: new() { ["en-US"] = "Shared_001=Shared text\n" });
        var dependencies = new[] { new ModuleDependency { Id = "MFW_Common_Assets", Version = "1.0.0" } };

        var result = await ModuleDependencyResolver.ResolveAsync(dependencies, store, "fr-FR");

        var dep = Assert.Single(result.DependencyRestexts);
        Assert.Null(dep.RestextText);
        Assert.Equal("Shared_001=Shared text\n", dep.DefaultRestextText);
    }

    [Fact]
    public async Task ResolveAsync_LayoutAndVariableYamlsAreDependencyOnly_ModuleAppendsItsOwnAfter()
    {
        var store = new FakeAssetPackStore();
        store.Installed[("MFW_Common_Assets", "1.0.0")] = MakeContents(
            manifestYaml: "id: 'MFW_Common_Assets'\ntitle: 'x'\nversion: '1.0.0'\n",
            layoutYamls: ["layout_id: 'hub_shared'"],
            variablesYaml: "variables:\n  shared: bool\n");
        var dependencies = new[] { new ModuleDependency { Id = "MFW_Common_Assets", Version = "1.0.0" } };

        var result = await ModuleDependencyResolver.ResolveAsync(dependencies, store, "en-US");

        Assert.Single(result.LayoutChromeYamls);
        Assert.Single(result.AdditionalVariableYamls);
    }

    [Fact]
    public async Task ResolveAsync_MultipleDependencies_OneMissingOneInstalled_BothHandledIndependently()
    {
        var store = new FakeAssetPackStore();
        store.Installed[("PackA", "1.0.0")] = MakeContents(
            manifestYaml: "id: 'PackA'\ntitle: 'A'\nversion: '1.0.0'\n",
            restextByLocale: new() { ["en-US"] = "FromA=A's text\n" });
        var dependencies = new[]
        {
            new ModuleDependency { Id = "PackA", Version = "1.0.0" },
            new ModuleDependency { Id = "PackB", Version = "2.0.0" },
        };

        var result = await ModuleDependencyResolver.ResolveAsync(dependencies, store, "en-US");

        Assert.Single(result.DependencyRestexts);
        var warning = Assert.Single(result.MissingDependencyWarnings);
        Assert.Contains("PackB", warning);
    }
}
