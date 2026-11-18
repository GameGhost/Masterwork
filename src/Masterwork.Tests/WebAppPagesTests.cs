using System.Globalization;
using System.Text.Json;
using Masterwork.App.Shared.Services;

namespace Masterwork.Tests;

public class WebAppPagesTests
{
    private static readonly string Template = string.Join('\n',
        WebAppPages.Tokens.AppName, WebAppPages.Tokens.Description, WebAppPages.Tokens.ThemeColor, WebAppPages.Tokens.Splash);

    [Fact]
    public async Task HostPage_FillsEveryTokenFromTheFlavor()
    {
        var config = TestWhiteLabel.Config;

        var page = await WebAppPages.RenderHostPageAsync(Template, config);

        Assert.DoesNotContain("{{", page);
        Assert.Contains(config.ThemeColor, page);
        Assert.Contains("id=\"mws-splash\"", page);
        Assert.Contains("mws-app-credit", page);
        Assert.Contains(AppVersion.Display, page);
    }

    [Fact]
    public async Task HostPage_EncodesFlavorText()
    {
        var config = TestWhiteLabel.Config with { AppName = "A <b>&</b> \"name\"", GameName = "G <i>" };

        var page = await WebAppPages.RenderHostPageAsync(Template, config);

        Assert.DoesNotContain("<b>", page);
        Assert.DoesNotContain("G <i>", page);
        Assert.Contains("A &lt;b&gt;&amp;&lt;/b&gt;", page);
    }

    [Fact]
    public async Task HostPage_IsInTheDefaultLanguage_WhateverTheMachineCulture()
    {
        var english = await RenderIn("en-US");
        var french = await RenderIn("fr-CA");

        Assert.Equal(english, french);

        static async Task<string> RenderIn(string culture)
        {
            var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                return await WebAppPages.RenderHostPageAsync(Template, TestWhiteLabel.Config);
            }
            finally
            {
                (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
            }
        }
    }

    [Fact]
    public void Manifest_CarriesTheFlavorIdentityAndThemeIcons()
    {
        var config = TestWhiteLabel.Config;

        using var manifest = JsonDocument.Parse(WebAppPages.ManifestJson(config));
        var root = manifest.RootElement;

        Assert.Equal(config.AppName, root.GetProperty("name").GetString());
        Assert.Equal(config.ShortName, root.GetProperty("short_name").GetString());
        Assert.Equal(config.ThemeColor, root.GetProperty("theme_color").GetString());
        Assert.Equal(WebAppPages.Description(config), root.GetProperty("description").GetString());
        Assert.All(root.GetProperty("icons").EnumerateArray(),
            icon => Assert.StartsWith(ThemeModules.AssetBasePath + "/", icon.GetProperty("src").GetString()));
    }
}
