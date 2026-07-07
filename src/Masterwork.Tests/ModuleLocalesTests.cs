using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class ModuleLocalesTests
{
    [Fact]
    public void SelectRestext_PrefersPreferredLocale()
    {
        var byLocale = new Dictionary<string, string> { ["en-US"] = "en", ["es"] = "es-text" };
        Assert.Equal("es-text", ModuleLocales.SelectRestext(byLocale, "es"));
    }

    [Fact]
    public void SelectRestext_FallsBackToDefaultWhenPreferredMissing()
    {
        var byLocale = new Dictionary<string, string> { ["en-US"] = "en", ["es"] = "es-text" };
        Assert.Equal("en", ModuleLocales.SelectRestext(byLocale, "de-DE"));
    }

    [Fact]
    public void SelectRestext_FallsBackToWhateverExistsWhenNoDefault()
    {
        var byLocale = new Dictionary<string, string> { ["es"] = "es-text" };
        Assert.Equal("es-text", ModuleLocales.SelectRestext(byLocale, null));
    }

    [Fact]
    public void SelectRestext_NullWhenNothingAvailable()
    {
        Assert.Null(ModuleLocales.SelectRestext(new Dictionary<string, string>(), "en-US"));
    }

    [Fact]
    public void SortedLocales_EmptyMapDefaultsToDefaultLocale()
    {
        Assert.Equal([ModuleLocales.Default], ModuleLocales.SortedLocales(new Dictionary<string, string>()));
    }

    [Fact]
    public void SortedLocales_SortsAlphabetically()
    {
        var byLocale = new Dictionary<string, string> { ["es"] = "x", ["en-US"] = "y", ["de-DE"] = "z" };
        Assert.Equal(["de-DE", "en-US", "es"], ModuleLocales.SortedLocales(byLocale));
    }
}
