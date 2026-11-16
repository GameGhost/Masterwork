using System.Globalization;
using System.Net;
using System.Text.Json;
using Masterwork.App.Shared.Chrome;
using Masterwork.App.Shared.Resources;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Masterwork.App.Shared.Services;

/// <summary>
/// The flavor-specific pages a web head serves before any WebAssembly runs: the PWA manifest and the
/// host page's splash. The ASP.NET host serves them per request; the static head has them written at
/// build time by <c>Masterwork.App.Web.StaticPages</c>, since a static site can't render anything.
/// </summary>
/// <remarks>
/// Both are in the app's default language. A browser fetches the manifest once at install and keeps
/// it, and the splash shows before the player's saved language can be read, so following the
/// request's (or the build machine's) language would be arbitrary.
/// </remarks>
public static class WebAppPages
{
    /// <summary>Placeholders a static host page template may contain.</summary>
    public static class Tokens
    {
        public const string AppName = "{{AppName}}";
        public const string Description = "{{Description}}";
        public const string ThemeColor = "{{ThemeColor}}";
        public const string Splash = "{{Splash}}";
    }

    /// <summary>The PWA manifest, as the object to serialize.</summary>
    public static object Manifest(WhiteLabelConfig whiteLabel) => new
    {
        name = whiteLabel.AppName,
        short_name = whiteLabel.ShortName,
        description = Description(whiteLabel),
        start_url = "/",
        display = "standalone",
        background_color = whiteLabel.ThemeColor,
        theme_color = whiteLabel.ThemeColor,
        icons = new[]
        {
            // The two sizes browsers look for when offering to install, and for the home screen.
            new { src = $"{ThemeModules.AssetBasePath}/logo-192.png", sizes = "192x192", type = "image/png" },
            new { src = $"{ThemeModules.AssetBasePath}/logo.png", sizes = "512x512", type = "image/png" },
        },
    };

    /// <summary>The PWA manifest as JSON, for writing to a static file.</summary>
    public static string ManifestJson(WhiteLabelConfig whiteLabel) =>
        JsonSerializer.Serialize(Manifest(whiteLabel), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    /// <summary>The app's one-line description.</summary>
    public static string Description(WhiteLabelConfig whiteLabel) =>
        InDefaultCulture(() => string.Format(AppStrings.App_Description, whiteLabel.GameName));

    /// <summary>
    /// Fills a static host page template's <see cref="Tokens"/>. Text values are HTML-encoded;
    /// <see cref="Tokens.Splash"/> becomes the rendered <see cref="WebSplash"/> markup.
    /// </summary>
    public static async Task<string> RenderHostPageAsync(string template, WhiteLabelConfig whiteLabel)
    {
        var splash = await RenderSplashAsync(whiteLabel);
        return template
            .Replace(Tokens.AppName, WebUtility.HtmlEncode(whiteLabel.AppName))
            .Replace(Tokens.Description, WebUtility.HtmlEncode(Description(whiteLabel)))
            .Replace(Tokens.ThemeColor, WebUtility.HtmlEncode(whiteLabel.ThemeColor))
            .Replace(Tokens.Splash, splash);
    }

    private static async Task<string> RenderSplashAsync(WhiteLabelConfig whiteLabel)
    {
        var services = new ServiceCollection().AddSingleton(whiteLabel).BuildServiceProvider();
        await using var _ = services;
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var previous = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(AppUiCultures.Default);
            try
            {
                var output = await renderer.RenderComponentAsync<WebSplash>();
                return output.ToHtmlString();
            }
            finally
            {
                (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = previous;
            }
        });
    }

    private static string InDefaultCulture(Func<string> format)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(AppUiCultures.Default);
        try
        {
            return format();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
