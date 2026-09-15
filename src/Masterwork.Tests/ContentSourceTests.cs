using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class ContentSourceTests
{
    private static readonly ContentSource Native = new(
        "https://raw.githubusercontent.com/Owner/Repo/main/catalog.json",
        "https://github.com/Owner/Repo/releases/download");

    [Fact]
    public void SignatureUrl_IsDerived_NeverConfiguredSeparately()
    {
        Assert.Equal("https://raw.githubusercontent.com/Owner/Repo/main/catalog.json.sig", Native.SignatureUrl);
    }

    [Fact]
    public void ResolvePackageUrl_AppendsToTheConfiguredBase()
    {
        Assert.Equal(
            "https://github.com/Owner/Repo/releases/download/v0.4.0/cost-of-disease.mwm",
            Native.ResolvePackageUrl("v0.4.0/cost-of-disease.mwm"));
    }

    [Fact]
    public void ResolvePackageUrl_ToleratesATrailingSlashOnTheBase()
    {
        var withSlash = Native with { PackageBaseUrl = "https://github.com/Owner/Repo/releases/download/" };

        Assert.Equal(Native.ResolvePackageUrl("v1/x.mwm"), withSlash.ResolvePackageUrl("v1/x.mwm"));
    }

    [Fact]
    public void ResolvePackageUrl_OnTheWebHead_StaysOnTheAppsOwnOrigin()
    {
        var web = new ContentSource("https://play.example.com/content/catalog.json", "https://play.example.com/content/packages");

        Assert.StartsWith("https://play.example.com/content/packages/", web.ResolvePackageUrl("v1/x.mwm"));
    }

    // The base comes from the build, so even a catalog that somehow carried a host-shaped path can't
    // redirect a download — it's refused instead of concatenated.
    [Theory]
    [InlineData("https://evil.example.com/x.mwm")]
    [InlineData("//evil.example.com/x.mwm")]
    [InlineData("/rooted.mwm")]
    [InlineData("../escape.mwm")]
    [InlineData("a/../../escape.mwm")]
    [InlineData("back\\slash.mwm")]
    [InlineData("  ")]
    public void ResolvePackageUrl_RefusesAPathThatEscapesTheBase(string path)
    {
        Assert.Throws<ArgumentException>(() => Native.ResolvePackageUrl(path));
    }

    [Fact]
    public void ForAddedSource_ServesContentFromTheCatalogsOwnDirectory()
    {
        // A player-added source gets the same-host property structurally: packages resolve against
        // wherever its catalog lives, so it can't point at someone else's host either.
        var added = ContentSource.ForAddedSource("https://community.example.com/mw/catalog.json");

        Assert.Equal("https://community.example.com/mw/", added.PackageBaseUrl);
        Assert.Equal("https://community.example.com/mw/v2/thing.mwm", added.ResolvePackageUrl("v2/thing.mwm"));
    }
}
