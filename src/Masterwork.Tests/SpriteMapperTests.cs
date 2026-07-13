using Masterwork.Extractor;

namespace Masterwork.Tests;

public class SpriteMapperTests
{
    private static SpriteMapper MakeSpriteMapper(string json)
    {
        var tempFile = Path.GetTempFileName() + ".json";
        File.WriteAllText(tempFile, json);
        try
        {
            return SpriteMapper.FromJsonFile(tempFile);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public void SpriteTagWithTokenSuffix_ResolvesToBaseIdentifierUri()
    {
        // Regression: <sprite="StorybookToken" ...> isn't itself a sprite-map entry, but it's the
        // same game piece as "Storybook", which is (identifier "Storybook", spriteName
        // "44-storybook") — some call sites in the source reference it with the "Token" suffix,
        // some without. Both must resolve to the same icon.
        var mapper = MakeSpriteMapper("""
            { "TheCostOfDisease": [
                { "id": 41, "identifier": "Storybook", "EnglishName": "Storybook", "spriteName": "44-storybook" }
            ] }
            """);

        var runs = mapper.TryParseRichText("Return the <sprite=\"StorybookToken\" index=0> token.");

        Assert.NotNull(runs);
        var iconRun = Assert.Single(runs!, r => r.AssetRef is not null);
        Assert.Equal("icon://storybook", iconRun.AssetRef);
    }

    [Fact]
    public void SpriteTagWithTokenSuffix_NoMatchingBaseIdentifier_FallsBackToRawSlug()
    {
        // Guard: the "Token" suffix fallback only kicks in when stripping it yields a name that's
        // actually in the sprite map. "S1_HeartToken" has no "S1_Heart" entry, so it must keep its
        // own raw-slugified icon rather than accidentally matching something unrelated.
        var mapper = MakeSpriteMapper("""
            { "TheCostOfDisease": [
                { "id": 1, "identifier": "Heart", "EnglishName": "Heart", "spriteName": "02-heart" }
            ] }
            """);

        var runs = mapper.TryParseRichText("Return the <sprite=\"S1_HeartToken\" index=0> token.");

        Assert.NotNull(runs);
        var iconRun = Assert.Single(runs!, r => r.AssetRef is not null);
        Assert.Equal("icon://s1_hearttoken", iconRun.AssetRef);
    }
}
