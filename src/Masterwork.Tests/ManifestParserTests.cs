using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class ManifestParserTests
{
    [Fact]
    public void ParsesRequiredFields()
    {
        var manifest = new ManifestParser().Parse("""
            id: 'original.cost_of_disease'
            title: 'The Cost of Disease'
            version: '1.0.0'
            """);

        Assert.Equal("original.cost_of_disease", manifest.Id);
        Assert.Equal("The Cost of Disease", manifest.Title);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("original_scenario", manifest.ModuleType);
        Assert.Null(manifest.Description);
        Assert.Empty(manifest.Dependencies);
    }

    [Fact]
    public void ParsesTypeAndDescription()
    {
        var manifest = new ManifestParser().Parse("""
            id: 'MFW_Common_Assets'
            title: 'MFW Common Assets'
            version: '1.0.0'
            type: 'asset_pack'
            description: 'Shared icons, audio, and onboarding flow for MFW-family modules.'
            """);

        Assert.Equal("asset_pack", manifest.ModuleType);
        Assert.Equal("Shared icons, audio, and onboarding flow for MFW-family modules.", manifest.Description);
    }

    [Fact]
    public void ParsesDependencies()
    {
        var manifest = new ManifestParser().Parse("""
            id: 'original.cost_of_disease'
            title: 'The Cost of Disease'
            version: '1.0.0'
            dependencies:
            - id: 'MFW_Common_Assets'
              version: '>=1.0.0'
            """);

        var dep = Assert.Single(manifest.Dependencies);
        Assert.Equal("MFW_Common_Assets", dep.Id);
        Assert.Equal(">=1.0.0", dep.Version);
    }

    [Fact]
    public void MissingRequiredField_Throws()
    {
        Assert.Throws<MwsParseException>(() => new ManifestParser().Parse("""
            title: 'Missing an id'
            version: '1.0.0'
            """));
    }

    [Fact]
    public void UnmatchedField_Warns()
    {
        var warnings = new ModuleWarnings();
        new ManifestParser().Parse("""
            id: 'x'
            title: 'X'
            version: '1.0.0'
            unexpected_field: 'oops'
            """, warnings);

        Assert.Contains(warnings.Items, w => w.Kind == "unmatched_field");
    }
}
