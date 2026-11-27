using System.Text.Json;
using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class SaveEntrySerializationTests
{
    private static SaveEntry Entry(SessionVoice? voice) =>
        new("id", "m", "1.0.0", false, "Name", null, DateTimeOffset.UnixEpoch, "en-US", voice);

    [Fact]
    public void APlaythroughsVoice_RoundTrips_IncludingNoNarration()
    {
        foreach (var voice in new[] { new SessionVoice("fr-CA", "reader"), SessionVoice.Off })
        {
            var read = JsonSerializer.Deserialize<SaveEntry>(JsonSerializer.Serialize(Entry(voice)));

            Assert.Equal(voice, read?.Voice);
        }
    }

    [Fact]
    public void ASaveWrittenBeforeVoicesWereSaved_HasNoVoice_NotNoNarration()
    {
        var json = """{"Id":"id","ModuleId":"m","ModuleVersion":"1.0.0","IsAutosave":false,"DisplayName":null,"LastStateLabel":null,"LastPlayedUtc":"1970-01-01T00:00:00+00:00","Language":"en-US"}""";

        Assert.Null(JsonSerializer.Deserialize<SaveEntry>(json)?.Voice);
    }
}
