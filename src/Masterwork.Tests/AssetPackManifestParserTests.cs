using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class AssetPackManifestParserTests
{
    [Fact]
    public void ParsesRequiredFields()
    {
        var manifest = new AssetPackManifestParser().Parse("""
            id: 'MFW_Common_Assets'
            title: 'MFW Common Assets'
            version: '1.0.0'
            """);

        Assert.Equal("MFW_Common_Assets", manifest.Id);
        Assert.Equal("MFW Common Assets", manifest.Title);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Null(manifest.Description);
    }

    [Fact]
    public void ParsesDescription()
    {
        var manifest = new AssetPackManifestParser().Parse("""
            id: 'MFW_Common_Assets'
            title: 'MFW Common Assets'
            description: 'Shared icons, audio, and layout chrome for MFW-family modules.'
            version: '1.0.0'
            """);

        Assert.Equal("Shared icons, audio, and layout chrome for MFW-family modules.", manifest.Description);
    }

    [Fact]
    public void DefaultLocale_NotDeclared_FallsBackToModuleLocalesDefault()
    {
        var manifest = new AssetPackManifestParser().Parse("""
            id: 'x'
            title: 'X'
            version: '1.0.0'
            """);

        Assert.Equal(ModuleLocales.Default, manifest.DefaultLocale);
    }

    [Fact]
    public void DefaultLocale_Declared_IsParsed()
    {
        var manifest = new AssetPackManifestParser().Parse("""
            id: 'x'
            title: 'X'
            version: '1.0.0'
            default_locale: 'fr-FR'
            """);

        Assert.Equal("fr-FR", manifest.DefaultLocale);
    }

    [Fact]
    public void MissingRequiredField_Throws()
    {
        Assert.Throws<MwsParseException>(() => new AssetPackManifestParser().Parse("""
            title: 'Missing an id'
            version: '1.0.0'
            """));
    }

    [Fact]
    public void UnmatchedField_Warns()
    {
        var warnings = new ModuleWarnings();
        new AssetPackManifestParser().Parse("""
            id: 'x'
            title: 'X'
            version: '1.0.0'
            unexpected_field: 'oops'
            """, warnings);

        Assert.Contains(warnings.Items, w => w.Kind == "unmatched_field");
    }
}
