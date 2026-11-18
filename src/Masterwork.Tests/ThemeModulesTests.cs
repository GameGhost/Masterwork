using Masterwork.App.Shared.Services;
using Masterwork.App.Theme.Test;
using Microsoft.Extensions.DependencyInjection;

// Stamped the way a flavor's props stamp a host, so AddThemeModule is exercised end to end.
[assembly: ThemeModule(typeof(TestTheme))]

namespace Masterwork.Tests;

public class ThemeModulesTests
{
    [Fact]
    public void AddThemeModule_RegistersTheStampedFlavor()
    {
        var services = new ServiceCollection();

        var module = services.AddThemeModule(typeof(ThemeModulesTests).Assembly);

        Assert.IsType<TestTheme>(module);
        using var provider = services.BuildServiceProvider();
        Assert.Equal("Test Flavor", provider.GetRequiredService<WhiteLabelConfig>().AppName);
        Assert.Equal(typeof(TestScene), provider.GetRequiredService<IMainMenuTheme>().SceneComponentType);
    }

    [Fact]
    public void Load_AssemblyBuiltWithoutAFlavor_Throws()
    {
        // Shared itself is never stamped.
        var error = Assert.Throws<InvalidOperationException>(() => ThemeModules.Load(typeof(WhiteLabelConfig).Assembly));
        Assert.Contains("without a theme", error.Message);
    }

    [Fact]
    public void Load_MoreThanOneFlavor_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ThemeModules.Load(
            [new ThemeModuleAttribute(typeof(TestTheme)), new ThemeModuleAttribute(typeof(TestTheme))], "Host"));
        Assert.Contains("exactly one", error.Message);
    }

    [Fact]
    public void Load_TypeThatIsNotAThemeModule_Throws()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ThemeModules.Load(
            [new ThemeModuleAttribute(typeof(TestScene))], "Host"));
        Assert.Contains(nameof(IThemeModule), error.Message);
    }

    [Fact]
    public void PrimarySource_IsTheConfiguredUpstream()
    {
        var config = new TestTheme().Config;

        Assert.Equal(config.CatalogUrl, config.PrimarySource.CatalogUrl);
        Assert.Equal(config.PackageBaseUrl, config.PrimarySource.PackageBaseUrl);
    }
}
