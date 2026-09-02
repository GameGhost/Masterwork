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

    [Fact]
    public void TypeField_ToleratedAndNotFlaggedAsUnmatched()
    {
        var warnings = new ModuleWarnings();
        new AssetPackManifestParser().Parse("""
            type: 'assets'
            id: 'x'
            title: 'X'
            version: '1.0.0'
            """, warnings);

        Assert.Empty(warnings.Items);
    }

    [Fact]
    public void ParsesFormat_MatchingCurrentVersion_NoWarning()
    {
        var warnings = new ModuleWarnings();
        var manifest = new AssetPackManifestParser().Parse($"""
            type: 'assets'
            format: '{MwsFormatVersion.Current}'
            id: 'x'
            title: 'X'
            version: '1.0.0'
            """, warnings);

        Assert.Equal(MwsFormatVersion.Current, manifest.Format);
        Assert.Empty(warnings.Items);
    }

    [Fact]
    public void ParsesFormat_StaleVersion_Warns()
    {
        var warnings = new ModuleWarnings();
        var manifest = new AssetPackManifestParser().Parse("""
            type: 'assets'
            format: 'mws/0.3'
            id: 'x'
            title: 'X'
            version: '1.0.0'
            """, warnings);

        Assert.Equal("mws/0.3", manifest.Format);
        Assert.Contains(warnings.Items, w => w.Kind == "unexpected_format_version");
    }
}
