using Masterwork.App.Shared.Resources;
using Masterwork.App.Shared.Services;

namespace Masterwork.App.Web;

/// <summary>Serves the PWA manifest from the flavor's <see cref="WhiteLabelConfig"/>.</summary>
public static class WebAppManifest
{
    /// <summary>Maps <c>/manifest.json</c>.</summary>
    public static void MapWebAppManifest(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/manifest.json", (WhiteLabelConfig whiteLabel) => Results.Json(
            new
            {
                name = whiteLabel.AppName,
                short_name = whiteLabel.ShortName,
                description = DefaultLanguageDescription(whiteLabel.GameName),
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
            },
            contentType: "application/manifest+json"));
    }

    /// <summary>
    /// The description in the app's default language. A browser fetches the manifest once at install
    /// and keeps it, so following the request's language would be arbitrary — and without this the
    /// server's own OS language would decide.
    /// </summary>
    private static string DefaultLanguageDescription(string gameName)
    {
        // AppStrings selects its language from the thread's UI culture.
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(AppUiCultures.Default);
        try
        {
            return string.Format(AppStrings.App_Description, gameName);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}
