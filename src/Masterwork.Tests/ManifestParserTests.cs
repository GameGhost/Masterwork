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

    // ── localized title/description, thumbnail, info, entry, passages paths ─

    private const string LocalizedManifestYaml = """
        id: 'renegade.cost_of_disease'
        version: '0.1.0'
        languages:
          - 'en-US'
          - 'es'
        title:
          - en-US: 'The Cost of Disease'
          - es: 'El Costo de la Enfermedad'
        description:
          - en-US: 'A village beset by fever.'
        thumbnail:
          image: 'image://MFW_Scenario_1'
          border-inactive: 'image://MFW_Scenario_Unelected_Border'
          border-active: 'image://MFW_Scenario_Selected_Border'
        info:
          players-min: 2
          players-max: 4
          playtime:
          - en-US: '180 minutes'
          - es: '180 minutos'
        entry: 'Setup_01_PlayerCountSelect'
        passages: 'passages'
        passages_override: 'passages-override'
        style: 'assets/style.css'
        dependencies: []
        """;

    [Fact]
    public void LocalizedTitle_ResolvesToDefaultLocale_WhenNoPreferenceGiven()
    {
        var manifest = new ManifestParser().Parse(LocalizedManifestYaml);

        Assert.Equal("The Cost of Disease", manifest.Title);
        Assert.Equal("A village beset by fever.", manifest.Description);
    }

    [Fact]
    public void LocalizedTitle_ResolvesToPreferredLocale_WhenAvailable()
    {
        var manifest = new ManifestParser().Parse(LocalizedManifestYaml, preferredLocale: "es");

        Assert.Equal("El Costo de la Enfermedad", manifest.Title);
    }

    [Fact]
    public void LocalizedTitle_FallsBackToDefault_WhenPreferredLocaleMissing()
    {
        // "description" only has an en-US entry; requesting "es" should still resolve via the
        // ModuleLocales.Default fallback rather than coming back null.
        var manifest = new ManifestParser().Parse(LocalizedManifestYaml, preferredLocale: "es");

        Assert.Equal("A village beset by fever.", manifest.Description);
    }

    [Fact]
    public void ParsesThumbnailInfoEntryAndPassagePaths()
    {
        var manifest = new ManifestParser().Parse(LocalizedManifestYaml);

        Assert.Equal(["en-US", "es"], manifest.Languages);
        Assert.NotNull(manifest.Thumbnail);
        Assert.Equal("image://MFW_Scenario_1", manifest.Thumbnail!.Image);
        Assert.Equal("image://MFW_Scenario_Unelected_Border", manifest.Thumbnail.BorderInactive);
        Assert.Equal("image://MFW_Scenario_Selected_Border", manifest.Thumbnail.BorderActive);

        Assert.NotNull(manifest.Info);
        Assert.Equal(2, manifest.Info!.PlayersMin);
        Assert.Equal(4, manifest.Info.PlayersMax);
        Assert.Equal("180 minutes", manifest.Info.Playtime);

        Assert.Equal("Setup_01_PlayerCountSelect", manifest.Entry);
        Assert.Equal("passages", manifest.PassagesPath);
        Assert.Equal("passages-override", manifest.PassagesOverridePath);
        Assert.Equal("assets/style.css", manifest.StylePath);
    }

    [Fact]
    public void PassagesPaths_DefaultWhenNotDeclared()
    {
        var manifest = new ManifestParser().Parse("""
            id: 'x'
            title: 'X'
            version: '1.0.0'
            """);

        Assert.Equal("passages", manifest.PassagesPath);
        Assert.Equal("passages-override", manifest.PassagesOverridePath);
        Assert.Equal("assets/style.css", manifest.StylePath);
        Assert.Null(manifest.Thumbnail);
        Assert.Null(manifest.Info);
        Assert.Null(manifest.Entry);
        Assert.Empty(manifest.Languages);
    }

    [Fact]
    public void PlainScalarTitle_StillWorks_AlongsideLocalizedFields()
    {
        // Backward compat: a module that hasn't adopted localized title/description at all.
        var manifest = new ManifestParser().Parse("""
            id: 'x'
            title: 'Plain Title'
            version: '1.0.0'
            description: 'Plain description'
            """);

        Assert.Equal("Plain Title", manifest.Title);
        Assert.Equal("Plain description", manifest.Description);
    }
}
