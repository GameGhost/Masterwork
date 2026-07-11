using Masterwork.Extractor;

namespace Masterwork.Tests;

public class RestextCollectorTests
{
    [Fact]
    public void CollectPassage_HyphenatedPassageId_KeyIsSanitized()
    {
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["nodes"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = "Hello there" },
            },
        };

        collector.CollectPassage("TCOD-TownName", "000-TCOD-TownName.mws.yaml", dict);

        var entries = Assert.Single(collector.Passages).Entries;
        var entry = Assert.Single(entries);
        Assert.Equal("TCOD_TownName_001", entry.Key);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", entry.Key);
    }

    [Fact]
    public void CollectPassage_SpacedPassageId_KeyIsSanitized()
    {
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["nodes"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = "Hello there" },
            },
        };

        collector.CollectPassage("TITLE SCREEN", "000-TITLE-SCREEN.mws.yaml", dict);

        var entries = Assert.Single(collector.Passages).Entries;
        var entry = Assert.Single(entries);
        Assert.Equal("TITLE_SCREEN_001", entry.Key);
        Assert.Matches("^[A-Za-z_][A-Za-z0-9_]*$", entry.Key);
    }

    [Fact]
    public void CollectPassage_SanitizedKey_RoundTripsThroughDictAsRestextRef()
    {
        var collector = new RestextCollector();
        var dict = new Dictionary<string, object?>
        {
            ["nodes"] = new List<Dictionary<string, object?>>
            {
                new() { ["type"] = "text", ["value"] = "Hello there" },
            },
        };

        collector.CollectPassage("Preparations-TCOD", "000-Preparations-TCOD.mws.yaml", dict);

        var nodes = (List<Dictionary<string, object?>>)dict["nodes"]!;
        Assert.Equal("restext://Preparations_TCOD_001", nodes[0]["value"]);
    }
}
