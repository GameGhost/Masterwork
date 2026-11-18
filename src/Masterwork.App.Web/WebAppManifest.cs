using Masterwork.App.Shared.Services;

namespace Masterwork.App.Web;

/// <summary>Serves the PWA manifest from the flavor's <see cref="WhiteLabelConfig"/>.</summary>
public static class WebAppManifest
{
    /// <summary>Maps <c>/manifest.json</c>.</summary>
    public static void MapWebAppManifest(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/manifest.json", (WhiteLabelConfig whiteLabel) =>
            Results.Json(WebAppPages.Manifest(whiteLabel), contentType: "application/manifest+json"));
    }
}
