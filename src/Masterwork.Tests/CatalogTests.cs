using System.Text;
using Masterwork.ModuleFormat;

namespace Masterwork.Tests;

public class CatalogTests
{
    private const string ValidSha = "0123456789abcdef0123456789ABCDEF0123456789abcdef0123456789abcdef";

    private static CatalogDocument SampleDocument() => new()
    {
        Format = CatalogFormatVersion.Current,
        Title = "Test Source",
        Updated = new DateTimeOffset(2026, 9, 13, 0, 0, 0, TimeSpan.Zero),
        Entries =
        [
            new CatalogEntry
            {
                Type = "module",
                Id = "test.module",
                Title = "Test Module",
                Version = "1.2.3",
                Path = "v1.2.3/test.mwm",
                Sha256 = ValidSha,
                Size = 4096,
                Description = "A module.",
                Languages = ["en-US", "fr-CA"],
                Players = new CatalogPlayers(1, 4),
                Playtime = "180 minutes",
                Dependencies = [new ModuleDependency { Id = "test.assets", Version = "0.1.0" }],
            },
            new CatalogEntry
            {
                Type = "assets",
                Id = "test.assets",
                Title = "Test Assets",
                Version = "0.1.0",
                Path = "v0.1.0/test.mwassets",
                Sha256 = ValidSha,
                Size = 2048,
            },
        ],
    };

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);

    private static string MinimalJson(string entryBody) => $$"""
        {
          "format": "{{CatalogFormatVersion.Current}}",
          "title": "Test Source",
          "entries": [{{{entryBody}}}]
        }
        """;

    private const string ValidEntryBody = $$"""
        "type": "module",
        "id": "test.module",
        "title": "Test Module",
        "version": "1.0.0",
        "path": "v1.0.0/test.mwm",
        "sha256": "{{ValidSha}}",
        "size": 10
        """;

    [Fact]
    public void WriteThenParse_RoundTripsEveryField()
    {
        var original = SampleDocument();

        var parsed = CatalogParser.Parse(CatalogParser.Write(original));

        Assert.Equal(original.Title, parsed.Title);
        Assert.Equal(original.Updated, parsed.Updated);
        Assert.Equal(2, parsed.Entries.Count);

        var module = parsed.Entries[0];
        Assert.Equal("module", module.Type);
        Assert.Equal("Test Module", module.Title);
        Assert.Equal(["en-US", "fr-CA"], module.Languages);
        Assert.Equal(new CatalogPlayers(1, 4), module.Players);
        Assert.Equal("180 minutes", module.Playtime);
        Assert.Equal("test.assets", Assert.Single(module.Dependencies).Id);

        // An asset pack carries no player count — absent, not a zeroed-out range.
        Assert.Null(parsed.Entries[1].Players);
    }

    [Fact]
    public void Write_UsesSnakeCaseOnTheWire()
    {
        var json = Encoding.UTF8.GetString(CatalogParser.Write(SampleDocument()));

        Assert.Contains("\"path\"", json);
        Assert.DoesNotContain("\"Path\"", json);
    }

    [Fact]
    public void Write_OmitsUnsetOptionalFields()
    {
        // Neither entry sets a thumbnail, and the asset pack sets no playtime — nulls shouldn't be
        // written at all, so the published catalog stays readable.
        var json = Encoding.UTF8.GetString(CatalogParser.Write(SampleDocument()));

        Assert.DoesNotContain("thumbnail_url", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void Parse_UnknownFormat_IsRejected()
    {
        var json = Json("""{ "format": "mwcatalog/99", "title": "x", "entries": [] }""");

        var ex = Assert.Throws<CatalogParseException>(() => CatalogParser.Parse(json));
        Assert.Contains("mwcatalog/99", ex.Message);
    }

    [Fact]
    public void Parse_MalformedJson_IsRejected()
    {
        Assert.Throws<CatalogParseException>(() => CatalogParser.Parse(Json("{ not json")));
    }

    [Fact]
    public void Parse_UnknownEntryType_IsRejected()
    {
        var json = Json(MinimalJson(ValidEntryBody.Replace("\"module\"", "\"executable\"")));

        var ex = Assert.Throws<CatalogParseException>(() => CatalogParser.Parse(json));
        Assert.Contains("executable", ex.Message);
    }

    [Fact]
    public void Parse_MalformedSha256_IsRejectedUpFront()
    {
        var json = Json(MinimalJson(ValidEntryBody.Replace(ValidSha, "not-a-hash")));

        var ex = Assert.Throws<CatalogParseException>(() => CatalogParser.Parse(json));
        Assert.Contains("sha256", ex.Message);
    }

    // An entry must never be able to name a host, or to climb out of the content base its source
    // publishes from — that's what stops a tampered catalog rerouting a download.
    [Theory]
    [InlineData("https://evil.example.com/test.mwm")]
    [InlineData("http://evil.example.com/test.mwm")]
    [InlineData("//evil.example.com/test.mwm")]
    [InlineData("/absolute/test.mwm")]
    [InlineData("../../../etc/passwd")]
    [InlineData("v1.0.0/../../escape.mwm")]
    [InlineData("v1.0.0\\test.mwm")]
    [InlineData("")]
    public void Parse_PathThatEscapesTheContentBase_IsRejected(string path)
    {
        // Escaped going in, or a literal backslash would be read as a JSON escape sequence and the
        // path under test would never reach the parser intact.
        var json = Json(MinimalJson(ValidEntryBody.Replace("v1.0.0/test.mwm", path.Replace("\\", "\\\\"))));

        var ex = Assert.Throws<CatalogParseException>(() => CatalogParser.Parse(json));
        Assert.Contains("relative", ex.Message);
    }

    [Fact]
    public void Parse_ValidMinimalEntry_Succeeds()
    {
        var parsed = CatalogParser.Parse(Json(MinimalJson(ValidEntryBody)));

        var entry = Assert.Single(parsed.Entries);
        Assert.Equal("test.module", entry.Id);

        // Collection fields default to empty rather than null, so callers never have to null-check them.
        Assert.Empty(entry.Languages);
        Assert.Empty(entry.Dependencies);
        Assert.Null(entry.Players);
        Assert.Null(parsed.Updated);
    }
}
