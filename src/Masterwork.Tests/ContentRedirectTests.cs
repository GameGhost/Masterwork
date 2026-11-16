using Masterwork.App.Web.Api;

namespace Masterwork.Tests;

public class ContentRedirectTests
{
    [Theory]
    [InlineData("catalog.json", "/content/catalog.json")]
    [InlineData("catalog.json.sig", "/content/catalog.json.sig")]
    [InlineData("thumbnails/renegade.cost_of_disease.png", "/content/thumbnails/renegade.cost_of_disease.png")]
    [InlineData("packages/v0.4.0/cost-of-disease.mwm", "/content/packages/v0.4.0/cost-of-disease.mwm")]
    public void ContentApiRoutes_RedirectIntoTheSiteStorage(string path, string expected) =>
        Assert.Equal(expected, ContentRedirect.Target(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("index.html")]
    [InlineData("catalog.json/extra")]
    [InlineData("packages/")]
    [InlineData("packages")]
    [InlineData("packages/../index.html")]
    [InlineData("packages/v0.4.0/../../secrets")]
    [InlineData("packages//evil.example/x.mwm")]
    [InlineData("packages/https://evil.example/x.mwm")]
    [InlineData(@"packages/a\b.mwm")]
    [InlineData("thumbnails/../catalog.json")]
    [InlineData("other/x.png")]
    public void AnythingElse_IsNotRedirected(string? path) =>
        Assert.Null(ContentRedirect.Target(path));

    [Fact]
    public void Target_EscapesEachSegment() =>
        Assert.Equal("/content/packages/v1/a%20b%3Fc%23d.mwm", ContentRedirect.Target("packages/v1/a b?c#d.mwm"));

    [Fact]
    public void Target_IsAlwaysOnThisSite()
    {
        // A Location starting with "//" would be read as another host.
        Assert.StartsWith("/content/", ContentRedirect.Target("packages/x.mwm"));
        Assert.Null(ContentRedirect.Target("packages//x.mwm"));
    }
}
