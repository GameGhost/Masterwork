using Masterwork.App.Shared.Services;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class AssetPackDependencyGuardTests
{
    private static InstalledModule MakeModule(string id, params ModuleDependency[] dependencies) =>
        new(id, "1.0.0", id, "", [], "sha", Dependencies: dependencies);

    [Fact]
    public void HasDependent_NoInstalledModules_ReturnsFalse()
    {
        var result = AssetPackDependencyGuard.HasDependent([], "MFW_Common_Assets", "1.0.0");

        Assert.False(result);
    }

    [Fact]
    public void HasDependent_NoModuleDeclaresThisDependency_ReturnsFalse()
    {
        var modules = new[] { MakeModule("cost-of-disease") };

        var result = AssetPackDependencyGuard.HasDependent(modules, "MFW_Common_Assets", "1.0.0");

        Assert.False(result);
    }

    [Fact]
    public void HasDependent_OneModuleDeclaresExactIdAndVersion_ReturnsTrue()
    {
        var modules = new[] { MakeModule("cost-of-disease", new ModuleDependency { Id = "MFW_Common_Assets", Version = "1.0.0" }) };

        var result = AssetPackDependencyGuard.HasDependent(modules, "MFW_Common_Assets", "1.0.0");

        Assert.True(result);
    }

    [Fact]
    public void HasDependent_ModuleDeclaresDifferentVersion_ReturnsFalse()
    {
        // Exact-pin dependency model — a module pinned to 1.0.0 doesn't count as a dependent of
        // the separately-installed 2.0.0 record.
        var modules = new[] { MakeModule("cost-of-disease", new ModuleDependency { Id = "MFW_Common_Assets", Version = "1.0.0" }) };

        var result = AssetPackDependencyGuard.HasDependent(modules, "MFW_Common_Assets", "2.0.0");

        Assert.False(result);
    }

    [Fact]
    public void HasDependent_OnlyOneOfSeveralModulesDependsOnIt_ReturnsTrue()
    {
        var modules = new[]
        {
            MakeModule("fear-of-the-unknown"),
            MakeModule("a-time-of-war", new ModuleDependency { Id = "MFW_Common_Assets", Version = "1.0.0" }),
        };

        var result = AssetPackDependencyGuard.HasDependent(modules, "MFW_Common_Assets", "1.0.0");

        Assert.True(result);
    }
}
