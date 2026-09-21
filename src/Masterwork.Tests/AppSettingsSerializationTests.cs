using System.Reflection;
using System.Text.Json;
using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

/// <summary>
/// <see cref="AppSettings"/> persists through <see cref="AppSettingsJson"/> on every head, so these
/// tests are what stand between a new setting and it being silently dropped. That has already
/// happened: the native store used to write a hand-maintained field list, and three settings were
/// never added to it — each appeared to work because the page held it in memory, and was lost on the
/// next reload.
/// </summary>
public class AppSettingsSerializationTests
{
    // Every setting given a value that differs from its default, so a round trip that drops or
    // mangles any one of them is visible. Extend this whenever AppSettings gains a property —
    // EveryProperty_IsCoveredByTheFixture fails until you do.
    private static AppSettings FullyPopulated() => new()
    {
        BgmVolume = 0.25,
        BgmMuted = true,
        SfxVolume = 0.75,
        SfxMuted = true,
        TextSizeStep = 4,
        UiLocale = "fr-CA",
        PreferredModuleLanguage = "fr-CA",
        TrustedPublisherThumbprints = ["AAAA1111", "BBBB2222"],
        CustomCatalogSources = ["https://beta.example.com/.catalog/catalog.json"],
        HiddenCatalogListings =
        [
            new HiddenCatalogListing(CatalogSourceKeys.Primary, "shared.module"),
            new HiddenCatalogListing("https://beta.example.com/.catalog/catalog.json", "other.module"),
        ],
    };

    private static IEnumerable<PropertyInfo> SettingProperties() =>
        typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void EveryProperty_IsCoveredByTheFixture()
    {
        // The guard against the bug recurring: a new setting that isn't given a non-default value
        // above can't be proven to persist, so this fails and says which one.
        var populated = FullyPopulated();
        var defaults = AppSettings.Default;

        foreach (var property in SettingProperties())
        {
            var populatedJson = JsonSerializer.Serialize(property.GetValue(populated));
            var defaultJson = JsonSerializer.Serialize(property.GetValue(defaults));

            Assert.True(
                populatedJson != defaultJson,
                $"AppSettings.{property.Name} still has its default value in FullyPopulated(); give it a non-default value so its persistence is actually tested.");
        }
    }

    [Fact]
    public void EveryProperty_SurvivesARoundTrip()
    {
        var original = FullyPopulated();

        var restored = AppSettingsJson.Deserialize(AppSettingsJson.Serialize(original));

        foreach (var property in SettingProperties())
        {
            Assert.True(
                JsonSerializer.Serialize(property.GetValue(original)) == JsonSerializer.Serialize(property.GetValue(restored)),
                $"AppSettings.{property.Name} did not survive a round trip.");
        }
    }

    [Fact]
    public void EveryProperty_HasAPublicSetter()
    {
        // A get-only property serializes out but can never be read back in, which is a silent loss
        // of the same kind — worth catching by name rather than by a confusing round-trip failure.
        foreach (var property in SettingProperties())
        {
            Assert.True(property.SetMethod?.IsPublic == true, $"AppSettings.{property.Name} has no public setter or init accessor.");
        }
    }

    [Fact]
    public void SettingsSavedBeforeAFieldExisted_LoadWithThatFieldsDefault()
    {
        // Adding a setting must never break loading one saved by an older build.
        var restored = AppSettingsJson.Deserialize("""{"BgmVolume":0.5}""");

        Assert.Equal(0.5, restored.BgmVolume);
        Assert.Empty(restored.TrustedPublisherThumbprints);
        Assert.Empty(restored.CustomCatalogSources);
        Assert.Empty(restored.HiddenCatalogListings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void UnreadableSettings_FallBackToDefaults_RatherThanThrowing(string? json)
    {
        // A corrupt settings value shouldn't stop the app starting.
        Assert.Equal(AppSettings.Default.UiLocale, AppSettingsJson.Deserialize(json).UiLocale);
    }
}
