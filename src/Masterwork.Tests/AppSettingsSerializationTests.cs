using System.Text.Json;
using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

/// <summary>
/// <see cref="AppSettings"/> is persisted as plain JSON by <c>LocalStorageAppSettingsStore</c>, so a
/// property that doesn't survive a round-trip silently loses the player's choice at the next launch.
/// </summary>
public class AppSettingsSerializationTests
{
    [Fact]
    public void TrustedPublisherThumbprints_SurviveARoundTrip()
    {
        var settings = AppSettings.Default with
        {
            TrustedPublisherThumbprints = ["AAAA1111", "BBBB2222"],
        };

        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));

        Assert.NotNull(restored);
        Assert.Equal(["AAAA1111", "BBBB2222"], restored.TrustedPublisherThumbprints);
    }

    [Fact]
    public void TrustedPublisherThumbprints_DefaultToEmpty_ForSettingsSavedBeforeTheFieldExisted()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("""{"BgmVolume":0.5}""");

        Assert.NotNull(restored);
        Assert.Empty(restored.TrustedPublisherThumbprints);
    }
}
